using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;
using Platform.Application.Common.Interfaces;
using Platform.Domain.Forms;
using Platform.Domain.Forms.Enums;

namespace Platform.Application.Forms;

/// <summary>
/// The submission checks that need to read other records, which SubmissionValueValidator
/// deliberately never does. Run by SubmitFormDataCommand and UpdateSubmissionCommand after
/// the validator, on the normalized values (Lookups as Guid, text trimmed).
///
///   A Lookup must point at a record that exists (not deleted) on its target form, and, when
///   it is filtered (FilterByFieldCode), that record must belong to the sibling this
///   submission picked - a zone of another project used to be accepted from the API.
///
///   A ShortText field with the "unique" rule must not repeat another record's value
///   (trimmed, ignoring case), within the UniqueWithin sibling's value when that is set.
///
/// Every Lookup's record is read in parallel, so the extra cost is one round trip for the
/// target forms' definitions plus one for the records, not one per field.
/// </summary>
public static class SubmissionDataChecks
{
    public const string ChoiceMissingCode = "submission.choiceMissing";
    public const string ChoiceMismatchCode = "submission.choiceMismatch";
    public const string DuplicateCode = "submission.duplicate";
    public const string ChoiceNotAllowedCode = "submission.choiceNotAllowed";

    public static async Task ThrowIfInvalidAsync(
        IApplicationDbContext db,
        IDynamicDataRepository repository,
        string tableName,
        IReadOnlyCollection<FieldDefinition> activeFields,
        IReadOnlyDictionary<string, object?> normalizedValues,
        Guid? recordId,
        CancellationToken cancellationToken)
    {
        var failures = new List<(string Code, ValidationFailure Failure)>();
        failures.AddRange(await CheckLookupsAsync(db, repository, activeFields, normalizedValues, cancellationToken));
        failures.AddRange(await CheckUniqueAsync(repository, tableName, activeFields, normalizedValues, recordId, cancellationToken));

        if (failures.Count != 0)
            throw new Common.Exceptions.ValidationException(failures[0].Code, failures.Select(f => f.Failure));
    }

    /// <summary>
    /// The lookup checks for one block of a repeatable section, run on the header and block
    /// values together (a block's lookup can be filtered by a header field such as project).
    /// Failures are keyed <c>{section}[{index}].{field}</c>.
    /// </summary>
    public static async Task ThrowIfBlockInvalidAsync(
        IApplicationDbContext db,
        IDynamicDataRepository repository,
        IReadOnlyCollection<FieldDefinition> sectionFields,
        IReadOnlyCollection<FieldDefinition> allFields,
        IReadOnlyDictionary<string, object?> mergedValues,
        string keyPrefix,
        CancellationToken cancellationToken)
    {
        var failures = await CheckLookupsAsync(db, repository, sectionFields, mergedValues, cancellationToken, allFields);
        if (failures.Count != 0)
            throw new Common.Exceptions.ValidationException(failures[0].Item1,
                failures.Select(f => new ValidationFailure(keyPrefix + f.Item2.PropertyName, f.Item2.ErrorMessage)));
    }

    private static async Task<List<(string, ValidationFailure)>> CheckLookupsAsync(
        IApplicationDbContext db,
        IDynamicDataRepository repository,
        IReadOnlyCollection<FieldDefinition> activeFields,
        IReadOnlyDictionary<string, object?> values,
        CancellationToken cancellationToken,
        IReadOnlyCollection<FieldDefinition>? labelFields = null)
    {
        // Single lookups give one id, multi lookups one entry per picked id.
        var chosen = activeFields
            .Where(f => f.FieldType is FieldType.Lookup or FieldType.MultiLookup && f.LookupFormDefinitionId is not null)
            .SelectMany(f => values.GetValueOrDefault(f.Code) switch
            {
                Guid one => new[] { (Field: f, Id: (Guid?)one) },
                IEnumerable<Guid> many => many.Select(id => (Field: f, Id: (Guid?)id)).ToArray(),
                _ => Array.Empty<(FieldDefinition Field, Guid? Id)>(),
            })
            .ToList();
        if (chosen.Count == 0) return [];

        var targetIds = chosen.Select(x => x.Field.LookupFormDefinitionId!.Value).Distinct().ToList();
        var targets = await db.FormDefinitions
            .Include(f => f.Versions).ThenInclude(v => v.Fields)
            .Include(f => f.Versions).ThenInclude(v => v.Sections)
            .Where(f => targetIds.Contains(f.Id))
            .ToDictionaryAsync(f => f.Id, cancellationToken);

        var lookups = chosen.Select(async x =>
        {
            var target = targets.GetValueOrDefault(x.Field.LookupFormDefinitionId!.Value);
            var targetFields = target?.GetPublishedVersion()?.Fields.Where(f => f.IsActive).ToList();
            if (target?.TableName is null || targetFields is null)
                return Missing(x.Field);

            // Only the column the filter compares is read; the rest of the row isn't needed.
            var filterField = string.IsNullOrWhiteSpace(x.Field.FilterByFieldCode)
                ? null
                : targetFields.SingleOrDefault(f =>
                    string.Equals(f.Code, x.Field.FilterByFieldCode, StringComparison.OrdinalIgnoreCase));
            var rowFilter = RowFilter.Parse(x.Field.RowFilterJson);
            var rowFilterField = rowFilter is null ? null : targetFields.SingleOrDefault(f => f.Code == rowFilter.Field);
            var readFields = new[] { filterField, rowFilterField }.OfType<FieldDefinition>().Distinct().ToList();
            var row = await repository.GetByIdAsync(target.TableName, readFields, x.Id!.Value, cancellationToken);
            if (row is null)
                return Missing(x.Field);

            if (rowFilter is not null && (rowFilterField is null || !rowFilter.Allows(row.Values.GetValueOrDefault(rowFilterField.Code))))
                return ((string, ValidationFailure)?)(ChoiceNotAllowedCode, new ValidationFailure(x.Field.Code,
                    $"The '{x.Field.Label}' you picked can't be chosen here."));

            if (filterField is not null &&
                !Equals(row.Values.GetValueOrDefault(filterField.Code), values.GetValueOrDefault(x.Field.FilterByFieldCode!)))
                return ((string, ValidationFailure)?)(ChoiceMismatchCode, new ValidationFailure(x.Field.Code,
                    $"The '{x.Field.Label}' you picked doesn't belong to the chosen '{SiblingLabel(labelFields ?? activeFields, x.Field.FilterByFieldCode!)}'."));

            return null;
        });

        return (await Task.WhenAll(lookups)).Where(r => r is not null).Select(r => r!.Value).ToList();

        static (string, ValidationFailure)? Missing(FieldDefinition field) =>
            (ChoiceMissingCode, new ValidationFailure(field.Code,
                $"The '{field.Label}' you picked no longer exists. Reload the page and choose again."));
    }

    private static async Task<List<(string, ValidationFailure)>> CheckUniqueAsync(
        IDynamicDataRepository repository,
        string tableName,
        IReadOnlyCollection<FieldDefinition> activeFields,
        IReadOnlyDictionary<string, object?> values,
        Guid? recordId,
        CancellationToken cancellationToken)
    {
        var failures = new List<(string, ValidationFailure)>();
        foreach (var field in activeFields.Where(f => f.FieldType == FieldType.ShortText))
        {
            var rules = SubmissionValueValidator.ParseRules(field.ValidationRulesJson);
            if (rules?.Unique != true || values.GetValueOrDefault(field.Code) is not string text) continue;

            // A UniqueWithin that no longer names an active field is ignored, which makes the
            // rule stricter (unique across the whole form), never looser.
            var scopeField = string.IsNullOrWhiteSpace(rules.UniqueWithin)
                ? null
                : activeFields.SingleOrDefault(f => string.Equals(f.Code, rules.UniqueWithin, StringComparison.OrdinalIgnoreCase));

            if (await repository.AnyOtherWithTextAsync(
                    tableName, field.Code, text,
                    scopeField?.Code, scopeField is null ? null : values.GetValueOrDefault(scopeField.Code),
                    recordId, cancellationToken))
                failures.Add((DuplicateCode, new ValidationFailure(field.Code, scopeField is null
                    ? $"'{text}' is already used as '{field.Label}' by another record."
                    : $"'{text}' is already used as '{field.Label}' by another record with the same '{scopeField.Label}'.")));
        }
        return failures;
    }

    private static string SiblingLabel(IEnumerable<FieldDefinition> fields, string code) =>
        fields.FirstOrDefault(f => string.Equals(f.Code, code, StringComparison.OrdinalIgnoreCase))?.Label ?? code;
}

/// <summary>A Lookup's row filter (FieldDefinition.RowFilterJson): only target rows whose
/// <see cref="Field"/> is one of <see cref="Values"/> are offered and accepted.</summary>
public sealed record RowFilter(string Field, IReadOnlyList<string> Values)
{
    public static RowFilter? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("field", out var f) || f.GetString() is not { Length: > 0 } field) return null;
            var values = root.TryGetProperty("values", out var v) && v.ValueKind == System.Text.Json.JsonValueKind.Array
                ? v.EnumerateArray().Select(e => e.ValueKind == System.Text.Json.JsonValueKind.String ? e.GetString() : e.GetRawText())
                    .OfType<string>().ToList()
                : new List<string>();
            return new RowFilter(field, values);
        }
        catch (System.Text.Json.JsonException) { return null; }
    }

    public bool Allows(object? value)
    {
        var text = value switch
        {
            null => null,
            bool b => b ? "true" : "false",
            IFormattable formattable => formattable.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
            _ => value.ToString(),
        };
        return text is not null && Values.Contains(text, StringComparer.Ordinal);
    }
}
