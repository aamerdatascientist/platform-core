using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Platform.Application.Common.Interfaces;

namespace Platform.Application.Forms.Assignments.Queries.GetAssignmentDays;

/// <summary>
/// The tracking list (administrators): one row per assigned user, form and working day in
/// [From, To] - filled or not, when they were assigned and when they filled it. Optionally
/// one user's or one form's.
/// </summary>
public record GetAssignmentDaysQuery(DateOnly From, DateOnly To, Guid? UserId = null, Guid? FormDefinitionId = null)
    : IRequest<IReadOnlyList<AssignmentDayDto>>;

public class GetAssignmentDaysQueryValidator : AbstractValidator<GetAssignmentDaysQuery>
{
    public const int MaxDays = 366;

    public GetAssignmentDaysQueryValidator()
    {
        RuleFor(x => x.To).GreaterThanOrEqualTo(x => x.From).WithMessage("'To' can't be before 'From'.");
        RuleFor(x => x).Must(x => x.To.DayNumber - x.From.DayNumber < MaxDays)
            .WithMessage($"Pick a range of at most {MaxDays} days.");
    }
}

public class GetAssignmentDaysQueryHandler : IRequestHandler<GetAssignmentDaysQuery, IReadOnlyList<AssignmentDayDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly IDynamicDataRepository _dynamicData;

    public GetAssignmentDaysQueryHandler(IApplicationDbContext db, IDynamicDataRepository dynamicData)
    {
        _db = db;
        _dynamicData = dynamicData;
    }

    public async Task<IReadOnlyList<AssignmentDayDto>> Handle(GetAssignmentDaysQuery request, CancellationToken cancellationToken)
    {
        var (assignments, stamps) = await FormAssignmentTracker.LoadAsync(
            _db, _dynamicData, request.UserId, request.FormDefinitionId, request.From, request.To, cancellationToken);
        var days = FormAssignmentTracker.BuildDays(
            assignments, stamps, request.From, request.To, FormAssignmentTracker.SaudiToday(DateTime.UtcNow));
        if (days.Count == 0) return days;

        // WhatsApp reminders about each day, sent or failed.
        var ids = assignments.Select(a => a.Id).ToList();
        var reminders = (await _db.NotificationLogs
                .Where(n => n.AssignmentId != null && ids.Contains(n.AssignmentId.Value) && n.ForDate != null)
                .Where(n => n.ForDate >= request.From && n.ForDate <= request.To)
                .Select(n => new { AssignmentId = n.AssignmentId!.Value, Date = n.ForDate!.Value, n.Status, n.SentAtUtc })
                .ToListAsync(cancellationToken))
            .GroupBy(n => (n.AssignmentId, n.Date))
            .ToDictionary(g => g.Key, g => (
                Sent: g.Count(n => n.Status == "sent"),
                Failed: g.Count(n => n.Status == "failed"),
                Last: g.Where(n => n.Status == "sent").Max(n => n.SentAtUtc)));

        return days
            .Select(d => reminders.TryGetValue((d.AssignmentId, d.Date), out var r)
                ? d with { RemindersSent = r.Sent, RemindersFailed = r.Failed, LastRemindedAtUtc = r.Last }
                : d)
            .ToList();
    }
}
