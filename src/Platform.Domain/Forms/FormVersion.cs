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

    /// <summary>
    /// No draft-only guard: once a form has been published, field edits apply directly to
    /// the live published version and run their DDL immediately (see
    /// AddFieldDefinitionCommand's published branch). The draft cycle still exists and is
    /// unchanged for a form's first build-out, before its first publish - it's just no
    /// longer the only way to change a field. DisplayOrder is deliberately _fields.Count
    /// rather than max+1: RemoveField is the only thing that can leave a gap, and it only
    /// runs on a never-published draft where order is still being assembled.
    /// </summary>
    public FieldDefinition AddField(string code, string label, FieldType type, bool isRequired,
        string? optionsJson, Guid? lookupFormDefinitionId, string? validationRulesJson)
    {
        if (_fields.Any(f => f.Code == code))
            throw new InvalidOperationException($"Field code '{code}' already exists on this version.");

        var field = FieldDefinition.Create(Id, code, label, type, isRequired,
            NextDisplayOrder(), optionsJson, lookupFormDefinitionId, validationRulesJson);
        _fields.Add(field);
        return field;
    }

    /// <summary>Max+1 rather than Count, so a removed field can't leave two fields sharing
    /// an order - the exact side effect the old remove-and-re-add edit mechanism caused.</summary>
    private int NextDisplayOrder() => _fields.Count == 0 ? 0 : _fields.Max(f => f.DisplayOrder) + 1;

    public void DeactivateField(Guid fieldDefinitionId) =>
        _fields.Single(f => f.Id == fieldDefinitionId).Deactivate();

    /// <summary>
    /// Applies an explicit order. Every active field must appear exactly once - a partial
    /// list would silently leave the omitted fields at whatever order they had, which reads
    /// as "the reorder half-worked" rather than a clean failure. Archived (inactive) fields
    /// keep their stored order and aren't part of the list, since nothing displays them.
    /// </summary>
    public void ReorderFields(IReadOnlyList<Guid> orderedFieldIds)
    {
        var activeIds = _fields.Where(f => f.IsActive).Select(f => f.Id).ToHashSet();

        if (orderedFieldIds.Count != activeIds.Count || !orderedFieldIds.ToHashSet().SetEquals(activeIds))
            throw new InvalidOperationException(
                "The supplied order must list every active field on this version exactly once.");

        for (var i = 0; i < orderedFieldIds.Count; i++)
            _fields.Single(f => f.Id == orderedFieldIds[i]).UpdateDisplayOrder(i);
    }

    /// <summary>
    /// Drops the field from this version's metadata entirely. On a never-published draft
    /// that's all there is to it. On a published form this is only half of a real Delete -
    /// the physical column has to be dropped alongside it (see DeleteFieldCommand); use
    /// DeactivateField instead for the Archive path, which keeps both the column and its data.
    /// </summary>
    public void RemoveField(Guid fieldId)
    {
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
