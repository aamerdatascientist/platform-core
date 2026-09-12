using MediatR;
using Platform.Application.Common.Interfaces;

namespace Platform.Application.Analytics.Queries.GetCrewCountByProject;

public record GetCrewCountByProjectQuery : IRequest<IReadOnlyList<ProjectCrewCountDto>>;

public record ProjectCrewCountDto(Guid ProjectId, string ProjectCode, string ProjectName, DateTime LogDate, int TotalHeadcount);

/// <summary>
/// Deliberately Labor Log's summed headcount, not Daily Site Report's typed crew_count -
/// see the Phase 0 audit follow-up. The two are independently entered with no defined
/// relationship; reconciling them is a separate data-quality question, not this KPI's job.
/// </summary>
public class GetCrewCountByProjectQueryHandler : IRequestHandler<GetCrewCountByProjectQuery, IReadOnlyList<ProjectCrewCountDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly IDynamicDataRepository _dynamicDataRepository;
    private readonly IExecutiveOverviewRepository _executiveOverviewRepository;

    public GetCrewCountByProjectQueryHandler(
        IApplicationDbContext db, IDynamicDataRepository dynamicDataRepository, IExecutiveOverviewRepository executiveOverviewRepository)
    {
        _db = db;
        _dynamicDataRepository = dynamicDataRepository;
        _executiveOverviewRepository = executiveOverviewRepository;
    }

    public async Task<IReadOnlyList<ProjectCrewCountDto>> Handle(GetCrewCountByProjectQuery request, CancellationToken cancellationToken)
    {
        var laborLogForm = await ExecutiveOverviewSupport.FindPublishedFormAsync(_db, "labor-log", cancellationToken);
        if (laborLogForm is null) return Array.Empty<ProjectCrewCountDto>();

        var projectsForm = await ExecutiveOverviewSupport.FindPublishedFormAsync(_db, "projects", cancellationToken);
        var projectsById = projectsForm is null
            ? new Dictionary<Guid, (string Code, string Name)>()
            : await ExecutiveOverviewSupport.ResolveMasterDataAsync(
                _dynamicDataRepository, projectsForm, "project_code", "project_name", cancellationToken);

        var rows = await _executiveOverviewRepository.GetCrewCountByProjectAndDateAsync(laborLogForm.TableName!, cancellationToken);

        return rows
            .Select(row =>
            {
                var (code, name) = projectsById.GetValueOrDefault(row.ProjectId, (string.Empty, string.Empty));
                return new ProjectCrewCountDto(row.ProjectId, code, name, row.LogDate, row.TotalHeadcount);
            })
            .OrderByDescending(dto => dto.LogDate)
            .ThenBy(dto => dto.ProjectCode)
            .ToList();
    }
}
