using Platform.Domain.Common;

namespace Platform.Domain.Forms;

/// <summary>
/// A user is expected to fill a form once every working day (Saturday to Thursday) from
/// <see cref="StartDate"/> to <see cref="EndDate"/> (open-ended when null). Dates are Saudi
/// calendar days. Nothing is stored per day: whether a day was filled is read from the form's
/// own submissions (who submitted, and when), so a day can never disagree with the data.
///
/// CreatedAtUtc / CreatedByUserId (set by the audit interceptor) are when and by whom the
/// user was assigned. Stopping keeps the history; deleting is only for an assignment that
/// never started.
/// </summary>
public class FormAssignment : AuditableEntity
{
    public Guid FormDefinitionId { get; private set; }
    public Guid UserId { get; private set; }
    public DateOnly StartDate { get; private set; }
    public DateOnly? EndDate { get; private set; }
    public string? Note { get; private set; }

    private FormAssignment() { }

    public static FormAssignment Create(Guid formDefinitionId, Guid userId, DateOnly startDate, DateOnly? endDate, string? note)
    {
        if (endDate is { } end && end < startDate)
            throw new ArgumentException("The end date can't be before the start date.", nameof(endDate));

        return new FormAssignment
        {
            FormDefinitionId = formDefinitionId,
            UserId = userId,
            StartDate = startDate,
            EndDate = endDate,
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
        };
    }

    /// <summary>Whether the assignment covers <paramref name="day"/> or any later day.</summary>
    public bool IsOpenOn(DateOnly day) => EndDate is not { } end || end >= day;

    /// <summary>
    /// Ends the assignment after <paramref name="today"/>: today still counts, nothing after
    /// does. One that hasn't started yet has no history to keep, so it is removed instead.
    /// </summary>
    public void Stop(DateOnly today)
    {
        if (StartDate > today)
        {
            IsDeleted = true;
            return;
        }
        if (EndDate is not { } end || end > today) EndDate = today;
    }
}
