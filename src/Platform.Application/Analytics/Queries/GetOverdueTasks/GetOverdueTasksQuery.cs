using MediatR;
using Platform.Application.Common.Interfaces;

namespace Platform.Application.Analytics.Queries.GetOverdueTasks;

public record GetOverdueTasksQuery : IRequest<IReadOnlyList<OverdueTaskDto>>;

public record OverdueTaskDto(
    Guid TaskId, Guid ProjectId, string ProjectCode, string ProjectName,
    string TaskReference, string Description, string AssignedTo, string Priority, DateTime DueDateUtc);

/// <summary>
/// due_date &lt; now AND status != completed - completed_date is deliberately never
/// consulted. The two fields are independently settable (nothing enforces they agree - see
/// the Phase 0 audit), and "overdue" is meant to answer "is this still hanging over us right
/// now", which is entirely a function of status, not of whether/when it was ever completed.
///
/// This predicate lives here in C#, not in the repository's SQL - see
/// IExecutiveOverviewRepository.GetTaskRowsAsync's doc comment for why.
/// </summary>
public class GetOverdueTasksQueryHandler : IRequestHandler<GetOverdueTasksQuery, IReadOnlyList<OverdueTaskDto>>
{
    private const string CompletedStatus = "completed";

    private readonly IApplicationDbContext _db;
    private readonly IDynamicDataRepository _dynamicDataRepository;
    private readonly IExecutiveOverviewRepository _executiveOverviewRepository;

    public GetOverdueTasksQueryHandler(
        IApplicationDbContext db, IDynamicDataRepository dynamicDataRepository, IExecutiveOverviewRepository executiveOverviewRepository)
    {
        _db = db;
        _dynamicDataRepository = dynamicDataRepository;
        _executiveOverviewRepository = executiveOverviewRepository;
    }

    public async Task<IReadOnlyList<OverdueTaskDto>> Handle(GetOverdueTasksQuery request, CancellationToken cancellationToken)
    {
        var taskTrackingForm = await ExecutiveOverviewSupport.FindPublishedFormAsync(_db, "task-tracking", cancellationToken);
        if (taskTrackingForm is null) return Array.Empty<OverdueTaskDto>();

        var projectsForm = await ExecutiveOverviewSupport.FindPublishedFormAsync(_db, "projects", cancellationToken);
        var projectsById = projectsForm is null
            ? new Dictionary<Guid, (string Code, string Name)>()
            : await ExecutiveOverviewSupport.ResolveMasterDataAsync(
                _dynamicDataRepository, projectsForm, "project_code", "project_name", cancellationToken);

        var rows = await _executiveOverviewRepository.GetTaskRowsAsync(taskTrackingForm.TableName!, cancellationToken);
        var nowUtc = DateTime.UtcNow;

        return rows
            .Where(row => row.DueDateUtc is { } dueDate && dueDate < nowUtc && row.Status != CompletedStatus)
            .Select(row =>
            {
                var (code, name) = projectsById.GetValueOrDefault(row.ProjectId, (string.Empty, string.Empty));
                return new OverdueTaskDto(
                    row.TaskId, row.ProjectId, code, name, row.TaskReference, row.Description, row.AssignedTo, row.Priority,
                    row.DueDateUtc!.Value);
            })
            // Oldest due date first - the longest-overdue task is the most urgent.
            .OrderBy(dto => dto.DueDateUtc)
            .ToList();
    }
}
