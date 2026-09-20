using Platform.Domain.Common;
using Platform.Domain.Forms.Enums;

namespace Platform.Domain.Forms;

/// <summary>
/// One field on a form. Code becomes the physical column name (for every FieldType
/// except Attachment, which never gets a physical column - see FieldType).
///
/// Label, IsRequired, DisplayOrder and validation/options are pure metadata: changing them
/// never touches the physical table. (Label does feed the reporting view's column aliases,
/// so a relabel on a published form needs a view refresh to show up downstream - see
/// DynamicSchemaService.RefreshReportingViewAsync.)
///
/// Code and FieldType are different: each one describes the physical column, so the
/// mutators for them (RenameCode/ChangeType) are only valid when the caller is also running
/// the matching DDL in the same operation. Don't call them on their own.
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

    public void UpdateDisplayOrder(int displayOrder) => DisplayOrder = displayOrder;

    /// <summary>
    /// Code is the physical column name, so this is only ever valid alongside a real
    /// ALTER TABLE ... RENAME COLUMN on a published form's table (see
    /// RenameFieldCodeCommand) - the two have to move together or the metadata stops
    /// describing the actual column. Goes through the same NormalizeColumnName whitelist as
    /// Create for exactly the same reason: this value ends up interpolated into DDL.
    /// </summary>
    public void RenameCode(string code) => Code = NormalizeColumnName(code);

    /// <summary>
    /// Only valid alongside a real ALTER TABLE ... ALTER COLUMN ... TYPE, and only once the
    /// caller has confirmed every existing value actually converts (see
    /// ChangeFieldTypeCommand) - the domain can't see the stored data, so it enforces the
    /// per-type companion-data rules here and leaves convertibility to the caller.
    /// </summary>
    public void ChangeType(FieldType fieldType, string? optionsJson, Guid? lookupFormDefinitionId)
    {
        if (fieldType == FieldType.Dropdown && string.IsNullOrWhiteSpace(optionsJson))
            throw new ArgumentException("Dropdown fields require OptionsJson.", nameof(optionsJson));
        if (fieldType == FieldType.Lookup && lookupFormDefinitionId is null)
            throw new ArgumentException("Lookup fields require LookupFormDefinitionId.", nameof(lookupFormDefinitionId));

        FieldType = fieldType;
        // Companion data belongs to the type that needs it - carrying a stale options list
        // onto a Number field (or a stale target form onto a Dropdown) would leave the field
        // describing a shape it no longer has.
        OptionsJson = fieldType == FieldType.Dropdown ? optionsJson : null;
        LookupFormDefinitionId = fieldType == FieldType.Lookup ? lookupFormDefinitionId : null;
    }

    public void Reactivate() => IsActive = true;

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
