using Platform.Domain.Common;
using Platform.Domain.Forms.Enums;

namespace Platform.Domain.Forms;

/// <summary>
/// One field on a form. Code becomes the physical column name (for every FieldType
/// except Attachment, which never gets a physical column - see FieldType). Code is
/// immutable once created; Label, IsRequired, DisplayOrder, and validation/options can
/// still change on a draft version without touching the physical schema.
/// </summary>
public class FieldDefinition : AuditableEntity
{
    public Guid FormVersionId { get; private set; }

    /// <summary>Physical column name. Must match ^[a-z][a-z0-9_]{0,62}$ after normalization.</summary>
    public string Code { get; private set; } = default!;
    public string Label { get; private set; } = default!;
    public FieldType FieldType { get; private set; }
    public bool IsRequired { get; private set; }
    public bool IsActive { get; private set; } = true;
    public int DisplayOrder { get; private set; }

    /// <summary>JSON array of {value,label} - only meaningful for FieldType.Dropdown.</summary>
    public string? OptionsJson { get; private set; }

    /// <summary>Only meaningful for FieldType.Lookup - which other form this field references.</summary>
    public Guid? LookupFormDefinitionId { get; private set; }

    /// <summary>JSON object, e.g. {"min":0,"max":100,"regex":"^[A-Z]{2}\\d+$"}.</summary>
    public string? ValidationRulesJson { get; private set; }

    private FieldDefinition() { }

    public static FieldDefinition Create(Guid formVersionId, string code, string label, FieldType fieldType,
        bool isRequired, int displayOrder, string? optionsJson, Guid? lookupFormDefinitionId,
        string? validationRulesJson)
    {
        var normalizedCode = NormalizeColumnName(code);

        if (fieldType == FieldType.Dropdown && string.IsNullOrWhiteSpace(optionsJson))
            throw new ArgumentException("Dropdown fields require OptionsJson.", nameof(optionsJson));
        if (fieldType == FieldType.Lookup && lookupFormDefinitionId is null)
            throw new ArgumentException("Lookup fields require LookupFormDefinitionId.", nameof(lookupFormDefinitionId));

        return new FieldDefinition
        {
            FormVersionId = formVersionId,
            Code = normalizedCode,
            Label = label.Trim(),
            FieldType = fieldType,
            IsRequired = isRequired,
            DisplayOrder = displayOrder,
            OptionsJson = optionsJson,
            LookupFormDefinitionId = lookupFormDefinitionId,
            ValidationRulesJson = validationRulesJson
        };
    }

    public void Deactivate() => IsActive = false;

    public void UpdateLabel(string label) => Label = label.Trim();

    /// <summary>
    /// Column names come from user-entered field codes and end up interpolated into DDL
    /// (DDL identifiers can't be parameterized like values can) - so this is a hard
    /// security boundary, not just cosmetic normalization. Anything that doesn't survive
    /// this whitelist is rejected outright rather than "cleaned up".
    /// </summary>
    private static string NormalizeColumnName(string code)
    {
        var normalized = code.Trim().ToLowerInvariant().Replace(' ', '_');
        if (System.Text.RegularExpressions.Regex.IsMatch(normalized, "^[a-z][a-z0-9_]{0,62}$"))
            return normalized;

        throw new ArgumentException(
            $"Field code '{code}' must normalize to a valid SQL identifier " +
            "(letters, digits, underscore, starting with a letter, max 63 chars).");
    }
}
