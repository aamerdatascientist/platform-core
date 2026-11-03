using MediatR;
using Platform.Application.Common.Interfaces;

namespace Platform.Application.Forms.Assignments.Queries.GetFormAssignments;

/// <summary>
/// One assignment as the management list shows it. <paramref name="DaysDue"/> counts the
/// working days from its start to today (or its end); <paramref name="DaysFilled"/> how many
/// of those the user filled. Active = not ended before today.
/// </summary>
public record FormAssignmentDto(
    Guid Id, Guid UserId, string UserName, Guid FormId, string FormName,
    DateOnly StartDate, DateOnly? EndDate, string? Note, DateTime AssignedAtUtc, string? AssignedByName,
    bool IsActive, int DaysDue, int DaysFilled);

/// <summary>Every assignment (administrators), active ones first, newest first within each.</summary>
public record GetFormAssignmentsQuery : IRequest<IReadOnlyList<FormAssignmentDto>>;

public class GetFormAssignmentsQueryHandler : IRequestHandler<GetFormAssignmentsQuery, IReadOnlyList<FormAssignmentDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly IDynamicDataRepository _dynamicData;

    public GetFormAssignmentsQueryHandler(IApplicationDbContext db, IDynamicDataRepository dynamicData)
    {
        _db = db;
        _dynamicData = dynamicData;
    }

    public async Task<IReadOnlyList<FormAssignmentDto>> Handle(GetFormAssignmentsQuery request, CancellationToken cancellationToken)
    {
        var today = FormAssignmentTracker.SaudiToday(DateTime.UtcNow);
        var (assignments, stamps) = await FormAssignmentTracker.LoadAsync(
            _db, _dynamicData, null, null, DateOnly.MinValue, DateOnly.MaxValue.AddDays(-1), cancellationToken);
        var earliest = assignments.Count == 0 ? today : assignments.Min(a => a.StartDate);
        var days = FormAssignmentTracker.BuildDays(assignments, stamps, earliest, today, today)
            .Where(d => d.Status != FormAssignmentTracker.Pending)
            .GroupBy(d => d.AssignmentId)
            .ToDictionary(g => g.Key, g => (Due: g.Count(), Filled: g.Count(d => d.Status == FormAssignmentTracker.Filled)));

        return assignments
            .Select(a =>
            {
                var counts = days.GetValueOrDefault(a.Id);
                return new FormAssignmentDto(
                    a.Id, a.UserId, a.UserName, a.FormId, a.FormName, a.StartDate, a.EndDate, a.Note,
                    a.AssignedAtUtc, a.AssignedByName, a.EndDate is not { } end || end >= today, counts.Due, counts.Filled);
            })
            .OrderByDescending(a => a.IsActive)
            .ThenByDescending(a => a.AssignedAtUtc)
            .ToList();
    }
}
