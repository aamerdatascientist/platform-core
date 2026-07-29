using Platform.Domain.Common;
using Platform.Domain.Forms.Enums;

namespace Platform.Domain.Forms;

/// <summary>
/// One publish checkpoint of a form. FieldDefinitions here describe the field set as
/// edited in this version. Because the physical column for a field is keyed by
/// FieldDefinition.Code (stable across versions), carrying a field forward into a new
/// draft version and re-publishing is a no-op against the physical table - only
/// genuinely new fields trigger an ALTER TABLE. See DynamicSchemaService.
/// </summary>
public class FormVersion : AuditableEntity
{
    public Guid FormDefinitionId { get; private set; }
    public int VersionNumber { get; private set; }
    public FormStatus Status { get; private set; } = FormStatus.Draft;
    public DateTime? PublishedAtUtc { get; private set; }

    private readonly List<FieldDefinition> _fields = new();
    public IReadOnlyCollection<FieldDefinition> Fields => _fields.AsReadOnly();

    private FormVersion() { }

    public static FormVersion CreateInitialDraft(Guid formDefinitionId) => new()
    {
        FormDefinitionId = formDefinitionId,
        VersionNumber = 1,
        Status = FormStatus.Draft
    };

    public static FormVersion CreateDraftFrom(Guid formDefinitionId, FormVersion? previous)
    {
        var draft = new FormVersion
        {
            FormDefinitionId = formDefinitionId,
            VersionNumber = (previous?.VersionNumber ?? 0) + 1,
            Status = FormStatus.Draft
        };

        if (previous is null) return draft;

        foreach (var field in previous._fields.Where(f => f.IsActive))
        {
            draft._fields.Add(FieldDefinition.Create(
                draft.Id, field.Code, field.Label, field.FieldType, field.IsRequired,
                field.DisplayOrder, field.OptionsJson, field.LookupFormDefinitionId, field.ValidationRulesJson));
        }

        return draft;
    }

    public FieldDefinition AddField(string code, string label, FieldType type, bool isRequired,
        string? optionsJson, Guid? lookupFormDefinitionId, string? validationRulesJson)
    {
        if (Status != FormStatus.Draft)
            throw new InvalidOperationException("Fields can only be added to a draft version.");
        if (_fields.Any(f => f.Code == code))
            throw new InvalidOperationException($"Field code '{code}' already exists on this version.");

        var field = FieldDefinition.Create(Id, code, label, type, isRequired,
            _fields.Count, optionsJson, lookupFormDefinitionId, validationRulesJson);
        _fields.Add(field);
        return field;
    }

    public void DeactivateField(Guid fieldDefinitionId) =>
        _fields.Single(f => f.Id == fieldDefinitionId).Deactivate();

    public void RemoveField(Guid fieldId)
    {
        if (Status != FormStatus.Draft)
            throw new InvalidOperationException("Fields can only be removed from a draft version.");

        var removed = _fields.RemoveAll(f => f.Id == fieldId);
        if (removed == 0)
            throw new InvalidOperationException("Field not found on this version.");
    }

    public void MarkPublished()
    {
        if (Status != FormStatus.Draft)
            throw new InvalidOperationException("Only a draft version can be published.");
        if (!_fields.Any(f => f.IsActive))
            throw new InvalidOperationException("Cannot publish a form version with no active fields.");

        Status = FormStatus.Published;
        PublishedAtUtc = DateTime.UtcNow;
    }
}
