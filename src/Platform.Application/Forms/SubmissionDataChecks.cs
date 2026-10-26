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

    private static async Task<List<(string, ValidationFailure)>> CheckLookupsAsync(
        IApplicationDbContext db,
        IDynamicDataRepository repository,
        IReadOnlyCollection<FieldDefinition> activeFields,
        IReadOnlyDictionary<string, object?> values,
        CancellationToken cancellationToken)
    {
        var chosen = activeFields
            .Where(f => f.FieldType == FieldType.Lookup && f.LookupFormDefinitionId is not null)
            .Select(f => (Field: f, Id: values.GetValueOrDefault(f.Code) as Guid?))
            .Where(x => x.Id is not null)
            .ToList();
        if (chosen.Count == 0) return [];

        var targetIds = chosen.Select(x => x.Field.LookupFormDefinitionId!.Value).Distinct().ToList();
        var targets = await db.FormDefinitions
            .Include(f => f.Versions).ThenInclude(v => v.Fields)
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
            var row = await repository.GetByIdAsync(
                target.TableName, filterField is null ? [] : [filterField], x.Id!.Value, cancellationToken);
            if (row is null)
                return Missing(x.Field);

            if (filterField is not null &&
                !Equals(row.Values.GetValueOrDefault(filterField.Code), values.GetValueOrDefault(x.Field.FilterByFieldCode!)))
                return ((string, ValidationFailure)?)(ChoiceMismatchCode, new ValidationFailure(x.Field.Code,
                    $"The '{x.Field.Label}' you picked doesn't belong to the chosen '{SiblingLabel(activeFields, x.Field.FilterByFieldCode!)}'."));

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
