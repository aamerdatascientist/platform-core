using MediatR;
using Platform.Application.Common.Interfaces;

namespace Platform.Application.Analytics.Queries.GetWeatherImpactedDays;

public record GetWeatherImpactedDaysQuery : IRequest<IReadOnlyList<ProjectWeatherSummaryDto>>;

public record ProjectWeatherSummaryDto(Guid ProjectId, string ProjectCode, string ProjectName, int TotalDays, int ImpactedDays);

public class GetWeatherImpactedDaysQueryHandler : IRequestHandler<GetWeatherImpactedDaysQuery, IReadOnlyList<ProjectWeatherSummaryDto>>
{
    private const string ClearWeatherValue = "clear";

    private readonly IApplicationDbContext _db;
    private readonly IDynamicDataRepository _dynamicDataRepository;
    private readonly IExecutiveOverviewRepository _executiveOverviewRepository;

    public GetWeatherImpactedDaysQueryHandler(
        IApplicationDbContext db, IDynamicDataRepository dynamicDataRepository, IExecutiveOverviewRepository executiveOverviewRepository)
    {
        _db = db;
        _dynamicDataRepository = dynamicDataRepository;
        _executiveOverviewRepository = executiveOverviewRepository;
    }

    public async Task<IReadOnlyList<ProjectWeatherSummaryDto>> Handle(
        GetWeatherImpactedDaysQuery request, CancellationToken cancellationToken)
    {
        var dailyReportForm = await ExecutiveOverviewSupport.FindPublishedFormAsync(_db, "daily-site-report", cancellationToken);
        if (dailyReportForm is null) return Array.Empty<ProjectWeatherSummaryDto>();

        var projectsForm = await ExecutiveOverviewSupport.FindPublishedFormAsync(_db, "projects", cancellationToken);
        var projectsById = projectsForm is null
            ? new Dictionary<Guid, (string Code, string Name)>()
            : await ExecutiveOverviewSupport.ResolveMasterDataAsync(
                _dynamicDataRepository, projectsForm, "project_code", "project_name", cancellationToken);

        var rows = await _executiveOverviewRepository.GetWeatherCountsByProjectAsync(dailyReportForm.TableName!, cancellationToken);

        // "Impacted" is every value except clear - including "other", which conflates
        // "unknown" with "actually impacted". That's a deliberate simplification for this
        // KPI, not an oversight - see the Phase 0 audit follow-up.
        return rows
            .GroupBy(row => row.ProjectId)
            .Select(group =>
            {
                var (code, name) = projectsById.GetValueOrDefault(group.Key, (string.Empty, string.Empty));
                var totalDays = group.Sum(r => r.DayCount);
                var impactedDays = group.Where(r => r.Weather != ClearWeatherValue).Sum(r => r.DayCount);
                return new ProjectWeatherSummaryDto(group.Key, code, name, totalDays, impactedDays);
            })
            .OrderByDescending(dto => dto.ImpactedDays)
            .ThenBy(dto => dto.ProjectCode)
            .ToList();
    }
}
