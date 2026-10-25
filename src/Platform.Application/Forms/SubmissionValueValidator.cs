using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Platform.Domain.Forms;
using Platform.Domain.Forms.Enums;

namespace Platform.Application.Forms;

/// <summary>
/// Runs before SubmitFormDataCommandHandler ever calls the dynamic data repository -
/// catching a bad value here means a clean, field-addressable 400, not a raw database
/// exception surfacing as a generic 500. Values arrive as boxed System.Text.Json.JsonElement
/// (the shape ASP.NET Core's model binder produces for Dictionary&lt;string, object?&gt;
/// request bodies), not native CLR types - every TryGet helper below handles both that and
/// already-native values defensively, since which one actually shows up isn't something
/// this code should have to assume correctly to stay safe.
/// </summary>
public static class SubmissionValueValidator
{
    private static readonly JsonSerializerOptions CaseInsensitive = new() { PropertyNameCaseInsensitive = true };

    // ValidationRulesJson's Regex is admin-authored, not developer-authored - a pattern with
    // catastrophic backtracking (accidental or not) would otherwise hang the request thread
    // on every submission against that field until the process itself is unresponsive.
    private static readonly TimeSpan RegexMatchTimeout = TimeSpan.FromMilliseconds(250);

    private const int ShortTextMaxLength = 400;

    /// <summary>
    /// The values as they should be stored: text is trimmed, and blank or whitespace-only text
    /// becomes null (so "   " is never saved as an answer). Run after <see cref="Validate"/>,
    /// on the same dictionary, before handing the values to the repository.
    /// </summary>
    public static IReadOnlyDictionary<string, object?> Normalize(
        IEnumerable<FieldDefinition> activeFields, IReadOnlyDictionary<string, object?> values)
    {
        var typeByCode = activeFields.ToDictionary(f => f.Code, f => f.FieldType, StringComparer.Ordinal);

        var result = new Dictionary<string, object?>(values.Count, StringComparer.Ordinal);
        foreach (var (code, raw) in values)
        {
            typeByCode.TryGetValue(code, out var type);
            if (type is FieldType.ShortText or FieldType.LongText or FieldType.Dropdown && TryGetString(raw, out var text))
            {
                var trimmed = text.Trim();
                result[code] = trimmed.Length == 0 ? null : trimmed;
            }
            // A number sent as text ("12.5") passes Validate, but the repository reads the raw
            // JSON as a number and threw - a 500 for any API caller that quotes its numbers.
            // Handing it the parsed value instead keeps both on the same reading.
            else if (type is FieldType.Number && TryGetDecimal(raw, out var whole))
            {
                result[code] = decimal.ToInt32(whole);
            }
            else if (type is FieldType.Decimal && TryGetDecimal(raw, out var number))
            {
                result[code] = number;
            }
            else
            {
                result[code] = raw;
            }
        }
        return result;
    }

    public static IDictionary<string, string[]> Validate(
        IEnumerable<FieldDefinition> activeFields, IReadOnlyDictionary<string, object?> values)
    {
        var errors = new Dictionary<string, List<string>>();
        void AddError(string code, string message)
        {
            if (!errors.TryGetValue(code, out var list))
            {
                list = new List<string>();
                errors[code] = list;
            }
            list.Add(message);
        }

        foreach (var field in activeFields.Where(f => f.FieldType != FieldType.Attachment))
        {
            values.TryGetValue(field.Code, out var raw);
            // Blank or whitespace-only text counts as not answered: "   " in a required
            // description is not a description.
            var isPresent = raw is not null && raw is not JsonElement { ValueKind: JsonValueKind.Null } &&
                            !(TryGetString(raw, out var asText) && string.IsNullOrWhiteSpace(asText));

            // A conditionally-hidden field (its controlling field's submitted value doesn't
            // match VisibleWhenValuesJson) is inapplicable to this submission, so it's never
            // required here even if IsRequired is set - that's what lets one branching Phase
            // dropdown carry several mutually-exclusive sets of "required" fields without
            // every branch's fields being required on every submission. Type/range checks
            // below still run if a value was sent anyway (defensive; the real frontend never
            // sends one for a field it isn't showing).
            if (field.IsRequired && !isPresent && IsFieldVisible(field, values))
            {
                AddError(field.Code, $"'{field.Label}' is required.");
                continue;
            }
            if (!isPresent) continue;

            var rules = ParseRules(field.ValidationRulesJson);

            switch (field.FieldType)
            {
                case FieldType.Number:
                    if (!TryGetDecimal(raw, out var numVal))
                        AddError(field.Code, $"'{field.Label}' must be a whole number.");
                    else if (numVal % 1 != 0)
                        AddError(field.Code, $"'{field.Label}' must be a whole number, not a decimal.");
                    // The column is a Postgres integer; anything outside it was a raw 500.
                    else if (numVal is > int.MaxValue or < int.MinValue)
                        AddError(field.Code, $"'{field.Label}' is too large.");
                    else
                        CheckRange(field.Label, numVal, rules, AddError, field.Code);
                    break;

                case FieldType.Decimal:
                    if (!TryGetDecimal(raw, out var decVal))
                        AddError(field.Code, $"'{field.Label}' must be a number.");
                    // numeric(18,4): at most 14 digits before the point.
                    else if (Math.Abs(decVal) >= 100_000_000_000_000m)
                        AddError(field.Code, $"'{field.Label}' is too large.");
                    else
                        CheckRange(field.Label, decVal, rules, AddError, field.Code);
                    break;

                case FieldType.Boolean:
                    if (!TryGetBool(raw, out _))
                        AddError(field.Code, $"'{field.Label}' must be yes or no.");
                    break;

                case FieldType.DateTime:
                    if (!TryGetString(raw, out var dateStr) || TryGetLocalDate(dateStr) is not { } localDate)
                        AddError(field.Code, $"'{field.Label}' must be a valid date.");
                    else if (localDate.Year is < 1900 or > 2200)
                        AddError(field.Code, $"'{field.Label}' must be a valid date.");
                    else if (rules?.NoFutureDates == true && localDate > SaudiToday())
                        AddError(field.Code, $"'{field.Label}' can't be in the future.");
                    break;

                case FieldType.Dropdown:
                    // A dynamic-options-source Dropdown (see FieldDefinition.
                    // DynamicOptionsSourceFormDefinitionId) has no fixed OptionsJson to check
                    // against by design - its valid set is whatever's currently in the source
                    // form, which can grow between when the frontend fetched it and when this
                    // submission arrives. Checked here as "any non-blank string", the same
                    // bar as a free-text field, rather than re-querying the source form from a
                    // validator that deliberately never reaches into dynamic data tables (see
                    // the Lookup case below).
                    if (!TryGetString(raw, out var optVal))
                        AddError(field.Code, $"'{field.Label}' must be one of the allowed options.");
                    else if (field.DynamicOptionsSourceFormDefinitionId is null && !IsAllowedDropdownValue(field.OptionsJson, optVal))
                        AddError(field.Code, $"'{field.Label}' must be one of the allowed options.");
                    break;

                case FieldType.Lookup:
                    // GUID-format check only - confirming the referenced record actually
                    // exists needs a query against the TARGET form's dynamic table, which
                    // this validator deliberately doesn't reach into. Malformed IDs from
                    // normal UI use shouldn't happen (the frontend only ever sends real IDs
                    // it fetched), so this catches direct-API misuse, not everyday use.
                    if (!TryGetString(raw, out var lookupVal) || !Guid.TryParse(lookupVal, out _))
                        AddError(field.Code, $"'{field.Label}' must reference a valid record.");
                    break;

                case FieldType.ShortText:
                case FieldType.LongText:
                    // ShortText is varchar(400); a longer value was a raw database error.
                    if (field.FieldType == FieldType.ShortText && TryGetString(raw, out var shortVal) && shortVal.Trim().Length > ShortTextMaxLength)
                        AddError(field.Code, $"'{field.Label}' must be at most {ShortTextMaxLength} characters.");
                    if (TryGetString(raw, out var textVal) && rules?.Regex is { Length: > 0 } pattern)
                    {
                        bool matches;
                        try { matches = Regex.IsMatch(textVal, pattern, RegexOptions.None, RegexMatchTimeout); }
                        // Same "malformed rule shouldn't block submission" philosophy as the
                        // RegexParseException case, applied to catastrophic backtracking too -
                        // an admin-authored pattern that runs away on some input is still not
                        // something the submitter should be blocked by.
                        catch (RegexParseException) { matches = true; }
                        catch (RegexMatchTimeoutException) { matches = true; }
                        if (!matches) AddError(field.Code, $"'{field.Label}' isn't in the expected format.");
                    }
                    break;
            }
        }

        return errors.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray());
    }

    /// <summary>
    /// Form dates are Saudi-local calendar dates (see DynamicDataRepository): a plain
    /// "yyyy-mm-dd" is that day; a timestamp with an explicit offset or Z is converted to
    /// Riyadh time first; one without an offset is already Riyadh time.
    /// </summary>
    private static readonly TimeSpan SaudiArabiaOffset = TimeSpan.FromHours(3);

    private static DateOnly SaudiToday() => DateOnly.FromDateTime(DateTime.UtcNow + SaudiArabiaOffset);

    private static DateOnly? TryGetLocalDate(string text)
    {
        if (!DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
            return null;
        var local = parsed.Kind switch
        {
            DateTimeKind.Utc => parsed + SaudiArabiaOffset,
            DateTimeKind.Local => parsed.ToUniversalTime() + SaudiArabiaOffset,
            _ => parsed,
        };
        return DateOnly.FromDateTime(local);
    }

    private static void CheckRange(string label, decimal value, ValidationRules? rules, Action<string, string> addError, string code)
    {
        if (rules?.Min is { } exclusiveMin && rules.MinExclusive == true)
        {
            if ((double)value <= exclusiveMin)
                addError(code, $"'{label}' must be more than {exclusiveMin.ToString(CultureInfo.InvariantCulture)}.");
        }
        else if (rules?.Min is { } min && (double)value < min)
            addError(code, $"'{label}' must be at least {min.ToString(CultureInfo.InvariantCulture)}.");
        if (rules?.Max is { } max && (double)value > max)
            addError(code, $"'{label}' must be at most {max.ToString(CultureInfo.InvariantCulture)}.");
    }

    private static bool TryGetString(object? raw, out string value)
    {
        switch (raw)
        {
            case string s:
                value = s;
                return true;
            case JsonElement { ValueKind: JsonValueKind.String } je:
                value = je.GetString() ?? "";
                return true;
            default:
                value = "";
                return false;
        }
    }

    private const NumberStyles PlainNumber =
        NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite |
        NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint;

    private static bool TryGetDecimal(object? raw, out decimal value)
    {
        switch (raw)
        {
            case decimal d: value = d; return true;
            case double db: value = (decimal)db; return true;
            case int i: value = i; return true;
            case long l: value = l; return true;
            case JsonElement { ValueKind: JsonValueKind.Number } je when je.TryGetDecimal(out var d2):
                value = d2;
                return true;
            // Plain digits, an optional sign and decimal point only: NumberStyles.Any also took
            // "$5", "(5)" as -5, "1,000" and "1e3", none of which a person means as a quantity.
            case JsonElement { ValueKind: JsonValueKind.String } je
                when decimal.TryParse(je.GetString(), PlainNumber, CultureInfo.InvariantCulture, out var d3):
                value = d3;
                return true;
            default:
                value = 0;
                return false;
        }
    }

    private static bool TryGetBool(object? raw, out bool value)
    {
        switch (raw)
        {
            case bool b:
                value = b;
                return true;
            case JsonElement { ValueKind: JsonValueKind.True }:
                value = true;
                return true;
            case JsonElement { ValueKind: JsonValueKind.False }:
                value = false;
                return true;
            default:
                value = false;
                return false;
        }
    }

    /// <summary>
    /// True when field has no visibility condition, or its controlling field's submitted
    /// value is one of VisibleWhenValuesJson. Every "can't actually tell" case - the
    /// controlling field wasn't answered, or the stored condition JSON is malformed - returns
    /// false (not visible, so not required), matching this file's existing convention
    /// elsewhere of failing open toward not blocking the submitter over bad/missing admin
    /// metadata (see IsAllowedDropdownValue, ParseRules).
    ///
    /// Both sides are compared as text, so a Boolean controlling field (submitted as JSON
    /// true/false) matches an allowed value of "true"/"false". The allowed values are read
    /// tolerantly by <see cref="ParseAllowedValues"/> - the live Daily Progress Report stores
    /// some conditions as a single JSON string ("\"true\"") rather than an array, which this
    /// used to reject, leaving those fields never required. FormRenderer.tsx's
    /// parseAllowedValues must read them the same way.
    /// </summary>
    private static bool IsFieldVisible(FieldDefinition field, IReadOnlyDictionary<string, object?> values)
    {
        if (string.IsNullOrWhiteSpace(field.VisibleWhenFieldCode)) return true;

        if (!values.TryGetValue(field.VisibleWhenFieldCode, out var controllingRaw) ||
            ToComparableText(controllingRaw) is not { } controllingValue)
            return false;

        return ParseAllowedValues(field.VisibleWhenValuesJson).Contains(controllingValue);
    }

    private static string? ToComparableText(object? raw) => raw switch
    {
        null => null,
        string s => s,
        bool b => b ? "true" : "false",
        JsonElement { ValueKind: JsonValueKind.String } je => je.GetString(),
        JsonElement { ValueKind: JsonValueKind.True } => "true",
        JsonElement { ValueKind: JsonValueKind.False } => "false",
        JsonElement { ValueKind: JsonValueKind.Number } je => je.GetRawText(),
        JsonElement => null,
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => raw.ToString(),
    };

    /// <summary>
    /// The allowed values of a visibility condition. Accepts the documented shape (a JSON
    /// array, e.g. ["excavation","foundation"]) and the shapes found in live data: a single JSON
    /// string ("excavation"), a JSON string that itself holds an array, and bare true/false or
    /// numbers. Anything unreadable gives no values (field hidden, never required).
    /// </summary>
    public static IReadOnlyList<string> ParseAllowedValues(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<string>();
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.String)
            {
                var inner = root.GetString() ?? string.Empty;
                // A string holding an array: decode once more.
                if (inner.TrimStart().StartsWith('[')) return ParseAllowedValues(inner);
                return new[] { inner };
            }
            if (root.ValueKind == JsonValueKind.Array)
                return root.EnumerateArray().Select(e => ToComparableText(e)).OfType<string>().ToList();
            return ToComparableText(root) is { } single ? new[] { single } : Array.Empty<string>();
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }

    private static bool IsAllowedDropdownValue(string? optionsJson, string value)
    {
        if (string.IsNullOrWhiteSpace(optionsJson)) return true; // shouldn't normally happen - fail open, not closed
        try
        {
            var options = JsonSerializer.Deserialize<List<DropdownOption>>(optionsJson, CaseInsensitive);
            return options is null || options.Any(o => o.Value == value);
        }
        catch (JsonException)
        {
            return true; // malformed stored options shouldn't block submission
        }
    }

    private static ValidationRules? ParseRules(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<ValidationRules>(json, CaseInsensitive);
        }
        catch (JsonException)
        {
            return null; // malformed stored rules shouldn't block submission
        }
    }

    private record DropdownOption(string Value, string Label);
    /// <summary>
    /// FieldDefinition.ValidationRulesJson. Min/Max apply to Number and Decimal (MinExclusive
    /// makes Min a strict "more than", e.g. a quantity that must be above zero); Regex to
    /// ShortText and LongText; NoFutureDates to DateTime (judged on the Saudi calendar).
    /// </summary>
    internal record ValidationRules(double? Min, double? Max, string? Regex, bool? NoFutureDates = null, bool? MinExclusive = null);
}
