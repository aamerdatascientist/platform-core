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

    private readonly List<FormStep> _steps = new();
    /// <summary>The filling steps, in any order - sort by DisplayOrder. Empty = one default step.</summary>
    public IReadOnlyCollection<FormStep> Steps => _steps.AsReadOnly();

    private readonly List<FormSection> _sections = new();
    public IReadOnlyCollection<FormSection> Sections => _sections.AsReadOnly();

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

        // Steps are copied too (new ids), and each copied field is pointed at its copied step
        // below - forgetting either would silently flatten a multi-step form into one step the
        // moment a new version is started, the same trap as VisibleWhenFieldCode.
        var stepMap = new Dictionary<Guid, Guid>();
        foreach (var step in previous._steps.OrderBy(s => s.DisplayOrder))
        {
            var copy = FormStep.Create(draft.Id, step.TitleAr, step.TitleEn, step.DisplayOrder);
            draft._steps.Add(copy);
            stepMap[step.Id] = copy.Id;
        }

        // Sections too (new ids, same child table) - a field copied without its section would
        // silently move from the child table's column to a parent column that doesn't exist.
        var sectionMap = new Dictionary<Guid, Guid>();
        foreach (var section in previous._sections)
        {
            var copy = FormSection.CopyFor(draft.Id, section);
            draft._sections.Add(copy);
            sectionMap[section.Id] = copy.Id;
        }

        foreach (var field in previous._fields.Where(f => f.IsActive))
        {
            // VisibleWhenFieldCode/VisibleWhenValuesJson/FilterByFieldCode carried forward
            // explicitly - easy to miss since this is a straight-up copy of every other
            // property, but silently dropping them would un-hide every conditional field and
            // un-filter every cascading Lookup the moment a new draft version is started, with
            // no error and no DDL to make the loss visible.
            draft._fields.Add(FieldDefinition.Create(
                draft.Id, field.Code, field.Label, field.FieldType, field.IsRequired,
                field.DisplayOrder, field.OptionsJson, field.LookupFormDefinitionId, field.ValidationRulesJson,
                field.VisibleWhenFieldCode, field.VisibleWhenValuesJson, field.FilterByFieldCode,
                field.DynamicOptionsSourceFormDefinitionId, field.DynamicOptionsSourceFieldCode, field.OptionsProvider));
            var copied = draft._fields[^1];
            copied.UpdateLabelEn(field.LabelEn);
            copied.SetVisibilityAttribute(field.VisibleWhenAttribute);
            copied.SetRowFilter(field.RowFilterJson);
            copied.SetFormula(field.Formula);
            copied.SetSection(field.SectionId is { } secId && sectionMap.TryGetValue(secId, out var newSecId) ? newSecId : null);
            copied.SetStep(field.StepId is { } sid && stepMap.TryGetValue(sid, out var newSid) ? newSid : null);
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
        string? optionsJson, Guid? lookupFormDefinitionId, string? validationRulesJson,
        string? visibleWhenFieldCode = null, string? visibleWhenValuesJson = null,
        string? filterByFieldCode = null, Guid? dynamicOptionsSourceFormDefinitionId = null,
        string? dynamicOptionsSourceFieldCode = null, string? labelEn = null, Guid? stepId = null,
        Guid? sectionId = null, string? optionsProvider = null)
    {
        if (sectionId is { } secId && _sections.All(s => s.Id != secId))
            throw new InvalidOperationException("That section isn't on this form version.");

        if (_fields.Any(f => f.Code == code))
            throw new InvalidOperationException($"Field code '{code}' already exists on this version.");

        // The "does this sibling actually exist" check belongs here, not in
        // FieldDefinition.Create: Create only knows the field being built, never the rest of
        // the version, and this is the one place that has both. Checked against _fields
        // (every field, not just active ones) rather than active-only, matching
        // FindFieldOrThrow's own archived-fields-included convention.
        if (!string.IsNullOrWhiteSpace(visibleWhenFieldCode) && !_fields.Any(f => f.Code == visibleWhenFieldCode))
            throw new InvalidOperationException(
                $"'{visibleWhenFieldCode}' isn't a field on this form version - a visibility " +
                "condition can only depend on a field that already exists here.");

        // Same check, same reason, for the Lookup-filter source field - this only confirms the
        // SAME-form sibling exists; whether the TARGET form actually has a matching-Code field
        // is a cross-aggregate check the handler does instead (see
        // AddFieldDefinitionCommandHandler/UpdateFieldLookupFilterCommandHandler), since this
        // entity has no visibility into other FormDefinitions.
        if (!string.IsNullOrWhiteSpace(filterByFieldCode) && !_fields.Any(f => f.Code == filterByFieldCode))
            throw new InvalidOperationException(
                $"'{filterByFieldCode}' isn't a field on this form version - a Lookup filter " +
                "can only depend on a field that already exists here.");

        var field = FieldDefinition.Create(Id, code, label, type, isRequired,
            NextDisplayOrder(), optionsJson, lookupFormDefinitionId, validationRulesJson,
            visibleWhenFieldCode, visibleWhenValuesJson, filterByFieldCode,
            dynamicOptionsSourceFormDefinitionId, dynamicOptionsSourceFieldCode, optionsProvider);
        field.SetSection(sectionId);
        if (stepId is { } sid && _steps.All(s => s.Id != sid))
            throw new InvalidOperationException("That step isn't on this form version.");
        field.UpdateLabelEn(labelEn);
        field.SetStep(stepId);
        _fields.Add(field);
        return field;
    }

    /// <summary>Max+1 rather than Count, so a removed field can't leave two fields sharing
    /// an order - the exact side effect the old remove-and-re-add edit mechanism caused.</summary>
    private int NextDisplayOrder() => _fields.Count == 0 ? 0 : _fields.Max(f => f.DisplayOrder) + 1;

    /// <summary>Used by UpdateFieldVisibilityCommand for the same sibling-exists check AddField
    /// runs inline - exposed separately since that command edits an existing field rather than
    /// adding one, so it has no other reason to call AddField itself.</summary>
    public bool HasField(string code) => _fields.Any(f => f.Code == code);

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

    // ------------------------------------------------------------------ steps (§9.2)
    // All Safe operations: metadata only, valid on a live published version. A step that is
    // returned is new - the handler must Add it to the DbContext explicitly (client-generated
    // keys defeat EF's new-vs-existing detection through the navigation; CLAUDE.md).

    public FormStep AddStep(string titleAr, string? titleEn)
    {
        var step = FormStep.Create(Id, titleAr, titleEn, _steps.Count == 0 ? 0 : _steps.Max(s => s.DisplayOrder) + 1);
        _steps.Add(step);
        return step;
    }

    public FormStep FindStepOrThrow(Guid stepId) =>
        _steps.SingleOrDefault(s => s.Id == stepId)
        ?? throw new KeyNotFoundException($"Step {stepId} is not on this form version.");

    /// <summary>Every step exactly once, like ReorderFields.</summary>
    public void ReorderSteps(IReadOnlyList<Guid> orderedStepIds)
    {
        if (orderedStepIds.Count != _steps.Count || !orderedStepIds.ToHashSet().SetEquals(_steps.Select(s => s.Id)))
            throw new InvalidOperationException("The supplied order must list every step on this version exactly once.");
        for (var i = 0; i < orderedStepIds.Count; i++)
            _steps.Single(s => s.Id == orderedStepIds[i]).SetDisplayOrder(i);
    }

    /// <summary>
    /// Removes a step. Its fields move to the previous step (HANDOFF §9.2); deleting the first
    /// step moves them to the step after it, and deleting the only step leaves them step-less
    /// (one default step). Returns the removed step so the handler can delete it.
    /// </summary>
    public FormStep RemoveStep(Guid stepId)
    {
        var ordered = _steps.OrderBy(s => s.DisplayOrder).ToList();
        var index = ordered.FindIndex(s => s.Id == stepId);
        if (index < 0) throw new KeyNotFoundException($"Step {stepId} is not on this form version.");
        var target = index > 0 ? ordered[index - 1] : ordered.Count > 1 ? ordered[1] : null;
        foreach (var field in _fields.Where(f => f.StepId == stepId))
            field.SetStep(target?.Id);
        var removed = ordered[index];
        _steps.Remove(removed);
        var i = 0;
        foreach (var step in _steps.OrderBy(s => s.DisplayOrder)) step.SetDisplayOrder(i++);
        return removed;
    }

    /// <summary>Moves a field to a step of this version (null = the first step).</summary>
    public void MoveFieldToStep(Guid fieldId, Guid? stepId)
    {
        if (stepId is { } id && _steps.All(s => s.Id != id))
            throw new KeyNotFoundException($"Step {id} is not on this form version.");
        var field = _fields.SingleOrDefault(f => f.Id == fieldId)
            ?? throw new KeyNotFoundException($"Field {fieldId} is not on this form version.");
        field.SetStep(stepId);
    }

    // --------------------------------------------------------- repeatable sections
    // Safe metadata, like steps; a returned section is new and must be Added explicitly.
    // Adding a section to a live form also needs its child table (the handler creates it).

    public FormSection AddSection(string code, string titleAr, string? titleEn, int minBlocks, int maxBlocks)
    {
        var section = FormSection.Create(Id, code, titleAr, titleEn, minBlocks, maxBlocks);
        if (_sections.Any(s => s.Code == section.Code))
            throw new InvalidOperationException($"A section with code '{section.Code}' already exists on this form.");
        _sections.Add(section);
        return section;
    }

    public FormSection FindSectionOrThrow(Guid sectionId) =>
        _sections.SingleOrDefault(s => s.Id == sectionId)
        ?? throw new KeyNotFoundException($"Section {sectionId} is not on this form version.");

    /// <summary>
    /// Moving a field in or out of a section moves where its column lives, so it's only
    /// allowed before the form is published (no table yet). On a live form, a section's
    /// fields are added to it directly (AddField with sectionId).
    /// </summary>
    public void MoveFieldToSection(Guid fieldId, Guid? sectionId)
    {
        if (Status != FormStatus.Draft)
            throw new InvalidOperationException("A field can only be moved in or out of a section before the form is published.");
        if (sectionId is { } id && _sections.All(s => s.Id != id))
            throw new KeyNotFoundException($"Section {id} is not on this form version.");
        var field = _fields.SingleOrDefault(f => f.Id == fieldId)
            ?? throw new KeyNotFoundException($"Field {fieldId} is not on this form version.");
        field.SetSection(sectionId);
    }

    /// <summary>A section with no fields left can be removed (its child table, if any, stays).</summary>
    public FormSection RemoveSection(Guid sectionId)
    {
        var section = FindSectionOrThrow(sectionId);
        if (_fields.Any(f => f.SectionId == sectionId))
            throw new InvalidOperationException("A section can only be removed once it has no fields at all (archived fields keep their data in it).");
        _sections.Remove(section);
        return section;
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
