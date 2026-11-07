using Microsoft.EntityFrameworkCore;
using Platform.Application.Common.Interfaces;

namespace Platform.Application.Forms.Assignments;

/// <summary>One assignment, with the names the pages show already resolved.</summary>
public record AssignmentInfo(
    Guid Id, Guid UserId, string UserName, Guid FormId, string FormName,
    DateOnly StartDate, DateOnly? EndDate, string? Note, DateTime AssignedAtUtc, string? AssignedByName);

/// <summary>
/// One working day of one assignment. <paramref name="Status"/>: "filled" (the user submitted
/// the form that Saudi day; <paramref name="FilledAtUtc"/> is the first time), "missed" (the
/// day is over with nothing submitted) or "pending" (today, nothing yet).
/// <paramref name="RemindersSent"/> / <paramref name="RemindersFailed"/>: WhatsApp reminders about
/// that day (filled in by the tracking query only).
/// </summary>
public record AssignmentDayDto(
    Guid AssignmentId, Guid UserId, string UserName, Guid FormId, string FormName, DateOnly Date,
    DateTime AssignedAtUtc, string? AssignedByName, string Status, DateTime? FilledAtUtc, int SubmissionCount,
    int RemindersSent = 0, int RemindersFailed = 0,
    /// <summary>The first entry sent that day, when filled (the grid cell opens it - redesign 23).</summary>
    Guid? RecordId = null,
    /// <summary>When the person was last reminded about that day, automatic or manual ("Reminded at 4:10 PM").</summary>
    DateTime? LastRemindedAtUtc = null);

/// <summary>
/// Turns assignments into days, and days into filled / missed / pending by reading the form's
/// own submissions (who created each row, and when). The working week is Saturday to
/// Thursday; days are Saudi calendar days, the same calendar the form data uses.
/// </summary>
public static class FormAssignmentTracker
{
    public const string Filled = "filled";
    public const string Missed = "missed";
    public const string Pending = "pending";

    /// <summary>How late the offline queue may send an entry (SubmitFormDataCommand allows 3 days).</summary>
    public const int LateSendDays = 3;

    private static readonly TimeSpan SaudiArabiaOffset = TimeSpan.FromHours(3);

    public static DateOnly SaudiToday(DateTime utcNow) => SaudiDay(utcNow);

    public static DateOnly SaudiDay(DateTime utc) => DateOnly.FromDateTime(utc + SaudiArabiaOffset);

    /// <summary>The UTC instant a Saudi calendar day starts.</summary>
    public static DateTime SaudiDayStartUtc(DateOnly day) =>
        DateTime.SpecifyKind(day.ToDateTime(TimeOnly.MinValue) - SaudiArabiaOffset, DateTimeKind.Utc);

    public static bool IsWorkingDay(DateOnly day) => day.DayOfWeek != DayOfWeek.Friday;

    /// <summary>
    /// Every working day each assignment covers within [from, to], never past today, newest
    /// first. <paramref name="stamps"/> are the forms' submissions by the assigned users.
    /// </summary>
    public static IReadOnlyList<AssignmentDayDto> BuildDays(
        IEnumerable<AssignmentInfo> assignments,
        IReadOnlyCollection<(Guid FormId, SubmissionStamp Stamp)> stamps,
        DateOnly from, DateOnly to, DateOnly today)
    {
        var byFormUserDay = stamps
            .GroupBy(s => (s.FormId, s.Stamp.UserId, Day: SaudiDay(s.Stamp.CreatedAtUtc)))
            .ToDictionary(g => g.Key, g => g.Select(s => s.Stamp).OrderBy(s => s.CreatedAtUtc).ToList());

        var days = new List<AssignmentDayDto>();
        foreach (var a in assignments)
        {
            var first = a.StartDate > from ? a.StartDate : from;
            var last = Min(Min(to, today), a.EndDate ?? DateOnly.MaxValue);
            for (var day = first; day <= last; day = day.AddDays(1))
            {
                if (!IsWorkingDay(day)) continue;
                byFormUserDay.TryGetValue((a.FormId, a.UserId, day), out var sent);
                var status = sent is { Count: > 0 } ? Filled : day < today ? Missed : Pending;
                days.Add(new AssignmentDayDto(
                    a.Id, a.UserId, a.UserName, a.FormId, a.FormName, day, a.AssignedAtUtc, a.AssignedByName,
                    status, sent?.FirstOrDefault()?.CreatedAtUtc, sent?.Count ?? 0, RecordId: sent?.FirstOrDefault()?.RecordId));
            }
        }

        return days
            .OrderByDescending(d => d.Date)
            .ThenBy(d => d.UserName, StringComparer.CurrentCulture)
            .ThenBy(d => d.FormName, StringComparer.CurrentCulture)
            .ToList();
    }

    /// <summary>
    /// Assignments (optionally one user's / one form's) that overlap [from, to], with names
    /// resolved, plus the submissions needed to judge their days in that range.
    /// </summary>
    public static async Task<(IReadOnlyList<AssignmentInfo> Assignments, IReadOnlyList<(Guid FormId, SubmissionStamp Stamp)> Stamps)> LoadAsync(
        IApplicationDbContext db, IDynamicDataRepository dynamicData,
        Guid? userId, Guid? formId, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var assignments = await db.FormAssignments
            .Where(a => userId == null || a.UserId == userId)
            .Where(a => formId == null || a.FormDefinitionId == formId)
            .Where(a => a.StartDate <= to && (a.EndDate == null || a.EndDate >= from))
            .ToListAsync(cancellationToken);
        if (assignments.Count == 0) return (Array.Empty<AssignmentInfo>(), Array.Empty<(Guid, SubmissionStamp)>());

        var formIds = assignments.Select(a => a.FormDefinitionId).Distinct().ToList();
        // Names are looked up past the soft-delete filter: an assignment's history should still
        // say who and what after a user or form is removed.
        var forms = await db.FormDefinitions.IgnoreQueryFilters()
            .Where(f => formIds.Contains(f.Id))
            .Select(f => new { f.Id, f.Name, f.TableName })
            .ToDictionaryAsync(f => f.Id, cancellationToken);
        var userIds = assignments.SelectMany(a => new[] { a.UserId, a.CreatedByUserId }).Distinct().ToList();
        var userNames = await db.Users.IgnoreQueryFilters()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName, cancellationToken);

        var infos = assignments
            .Select(a => new AssignmentInfo(
                a.Id, a.UserId, userNames.GetValueOrDefault(a.UserId) ?? "?",
                a.FormDefinitionId, forms.GetValueOrDefault(a.FormDefinitionId)?.Name ?? "?",
                a.StartDate, a.EndDate, a.Note, a.CreatedAtUtc, userNames.GetValueOrDefault(a.CreatedByUserId)))
            .ToList();

        var stamps = new List<(Guid, SubmissionStamp)>();
        foreach (var group in assignments.GroupBy(a => a.FormDefinitionId))
        {
            if (forms.GetValueOrDefault(group.Key)?.TableName is not { } table) continue;
            var start = group.Min(a => a.StartDate) > from ? group.Min(a => a.StartDate) : from;
            // An entry kept on the phone without signal is sent up to 3 days later but counts for
            // the day it was entered (owner decision 8) - so read 3 days past the range too.
            var rows = await dynamicData.GetSubmissionStampsAsync(
                table, group.Select(a => a.UserId).Distinct().ToList(),
                SaudiDayStartUtc(start), SaudiDayStartUtc(to.AddDays(1 + LateSendDays)), cancellationToken);
            stamps.AddRange(rows.Select(r => (group.Key, r)));
        }

        // The phone's own time, where the entry came through the offline queue (SubmissionReceipt).
        var recordIds = stamps.Select(s => s.Item2.RecordId).OfType<Guid>().ToList();
        if (recordIds.Count > 0)
        {
            var entered = await db.SubmissionReceipts
                .Where(r => r.RecordId != null && recordIds.Contains(r.RecordId.Value) && r.EnteredAtUtc != null)
                .ToDictionaryAsync(r => r.RecordId!.Value, r => r.EnteredAtUtc!.Value, cancellationToken);
            for (var i = 0; i < stamps.Count; i++)
                if (stamps[i].Item2.RecordId is { } id && entered.TryGetValue(id, out var at))
                    stamps[i] = (stamps[i].Item1, stamps[i].Item2 with { CreatedAtUtc = DateTime.SpecifyKind(at, DateTimeKind.Utc) });
        }

        return (infos, stamps);
    }

    private static DateOnly Min(DateOnly a, DateOnly b) => a < b ? a : b;
}
