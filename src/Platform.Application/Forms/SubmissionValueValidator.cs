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
            var isPresent = raw is not null && raw is not JsonElement { ValueKind: JsonValueKind.Null };

            if (field.IsRequired && !isPresent)
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
                    else
                        CheckRange(field.Label, numVal, rules, AddError, field.Code);
                    break;

                case FieldType.Decimal:
                    if (!TryGetDecimal(raw, out var decVal))
                        AddError(field.Code, $"'{field.Label}' must be a number.");
                    else
                        CheckRange(field.Label, decVal, rules, AddError, field.Code);
                    break;

                case FieldType.Boolean:
                    if (!TryGetBool(raw, out _))
                        AddError(field.Code, $"'{field.Label}' must be yes or no.");
                    break;

                case FieldType.DateTime:
                    if (!TryGetString(raw, out var dateStr) ||
                        !DateTime.TryParse(dateStr, CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                        AddError(field.Code, $"'{field.Label}' must be a valid date.");
                    break;

                case FieldType.Dropdown:
                    if (!TryGetString(raw, out var optVal) || !IsAllowedDropdownValue(field.OptionsJson, optVal))
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
                    if (TryGetString(raw, out var textVal) && rules?.Regex is { Length: > 0 } pattern)
                    {
                        bool matches;
                        try { matches = Regex.IsMatch(textVal, pattern); }
                        catch (RegexParseException) { matches = true; } // malformed rule shouldn't block submission
                        if (!matches) AddError(field.Code, $"'{field.Label}' isn't in the expected format.");
                    }
                    break;
            }
        }

        return errors.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray());
    }

    private static void CheckRange(string label, decimal value, ValidationRules? rules, Action<string, string> addError, string code)
    {
        if (rules?.Min is { } min && (double)value < min)
            addError(code, $"'{label}' must be at least {min}.");
        if (rules?.Max is { } max && (double)value > max)
            addError(code, $"'{label}' must be at most {max}.");
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
            case JsonElement { ValueKind: JsonValueKind.String } je
                when decimal.TryParse(je.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var d3):
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
    private record ValidationRules(double? Min, double? Max, string? Regex);
}
