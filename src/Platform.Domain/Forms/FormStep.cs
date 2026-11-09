using Platform.Domain.Common;

namespace Platform.Domain.Forms;

/// <summary>
/// One step of a form as it is filled on a phone (redesign HANDOFF §9.2): an ordered,
/// titled group of fields on one FormVersion. Steps are presentation only - a submission is
/// still one request with every field, and nothing physical (no column, no table) depends on
/// them. A field points at its step through FieldDefinition.StepId; a field with no step goes
/// into the first step (or, on a form with no steps at all, the single default step whose
/// title is the form name).
///
/// Same lifecycle as the fields: a published form edits its live version directly, and
/// FormVersion.CreateDraftFrom copies the steps (with new ids) and re-points each copied
/// field at its copied step.
/// </summary>
public class FormStep : AuditableEntity
{
    public Guid FormVersionId { get; private set; }

    /// <summary>The Arabic title (required, like every other label in this app).</summary>
    public string TitleAr { get; private set; } = default!;

    /// <summary>The English title; null until someone fills it in (the UI falls back to Arabic).</summary>
    public string? TitleEn { get; private set; }

    public int DisplayOrder { get; private set; }

    private FormStep() { }

    internal static FormStep Create(Guid formVersionId, string titleAr, string? titleEn, int displayOrder)
    {
        var step = new FormStep { FormVersionId = formVersionId, DisplayOrder = displayOrder };
        step.Rename(titleAr, titleEn);
        return step;
    }

    public void Rename(string titleAr, string? titleEn)
    {
        if (string.IsNullOrWhiteSpace(titleAr))
            throw new ArgumentException("A step needs an Arabic title.", nameof(titleAr));
        TitleAr = titleAr.Trim();
        TitleEn = string.IsNullOrWhiteSpace(titleEn) ? null : titleEn.Trim();
    }

    internal void SetDisplayOrder(int displayOrder) => DisplayOrder = displayOrder;
}
