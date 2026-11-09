using Platform.Domain.Forms;
using Platform.Domain.Forms.Enums;

namespace Platform.Application.Forms;

/// <summary>
/// The settings added with stage progress (2026-11-10) - calculated formula, lookup row filter,
/// visibility by attribute, options provider - checked and applied in one place, for both
/// "add field" and "update field settings".
/// </summary>
public static class FieldExtras
{
    public static void Apply(
        FieldDefinition field, FormVersion version, IFieldOptionsProviders providers,
        string? formula, string? rowFilterJson, string? visibleWhenAttribute, string? optionsProvider)
    {
        var failures = new List<FluentValidation.Results.ValidationFailure>();

        if (!string.IsNullOrWhiteSpace(formula))
        {
            if (field.FieldType is not (FieldType.Number or FieldType.Decimal))
                failures.Add(new(nameof(formula), "Only a number field can be calculated."));
            else
            {
                try
                {
                    foreach (var code in FieldFormula.References(formula))
                    {
                        var input = version.Fields.FirstOrDefault(f => f.Code == code && f.IsActive);
                        if (input is null || input.FieldType is not (FieldType.Number or FieldType.Decimal) || input.Id == field.Id)
                            failures.Add(new(nameof(formula), $"'{code}' isn't another number field on this form."));
                    }
                }
                catch (FormatException ex) { failures.Add(new(nameof(formula), ex.Message)); }
            }
        }

        if (!string.IsNullOrWhiteSpace(rowFilterJson))
        {
            if (field.FieldType is not (FieldType.Lookup or FieldType.MultiLookup))
                failures.Add(new(nameof(rowFilterJson), "Only a Lookup field can filter its rows."));
            else if (RowFilter.Parse(rowFilterJson) is not { Values.Count: > 0 })
                failures.Add(new(nameof(rowFilterJson), "A row filter needs a field and at least one value."));
        }

        if (!string.IsNullOrWhiteSpace(visibleWhenAttribute) &&
            (field.VisibleWhenFieldCode is null || !System.Text.RegularExpressions.Regex.IsMatch(visibleWhenAttribute.Trim(), "^[a-z][a-z0-9_]{0,62}$")))
            failures.Add(new(nameof(visibleWhenAttribute), "An attribute needs a visibility condition and a Latin name."));

        if (!string.IsNullOrWhiteSpace(optionsProvider))
        {
            if (providers.Find(optionsProvider.Trim()) is null)
                failures.Add(new(nameof(optionsProvider), $"There's no options provider called '{optionsProvider}'."));
            else if (field.FieldType is not (FieldType.Dropdown or FieldType.MultiSelect or FieldType.Lookup or FieldType.MultiLookup))
                failures.Add(new(nameof(optionsProvider), "Only a choice field can take its options from a provider."));
        }

        if (failures.Count != 0) throw new Common.Exceptions.ValidationException(failures);

        field.SetFormula(formula);
        field.SetRowFilter(rowFilterJson);
        field.SetVisibilityAttribute(visibleWhenAttribute);
        field.SetOptionsProvider(optionsProvider);
    }
}
