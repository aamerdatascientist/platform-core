using MediatR;
using Platform.Application.Common.Interfaces;

namespace Platform.Application.Analytics.Queries.GetExecutiveOverview;

/// <summary>
/// The whole Executive Overview page in one request: portfolio tiles, the per-project table,
/// weekly trends, weather and delay causes, stock, and the latest problems - all read from
/// the seven forms that exist today (projects, project-zones, project-footings,
/// project-floors, daily-progress-report, stock-inflow, stock-outflow).
///
/// <paramref name="ProjectId"/> narrows everything except the project table itself (which
/// always lists every reporting project, since it is also what the user picks from).
/// <paramref name="Phase"/> narrows the same way, to one stage's reports; an unknown value is
/// ignored rather than rejected, so an old link never breaks the page.
///
/// Replaces the five older per-panel analytics queries, which read forms that were deleted in
/// the 2026-10-04 projects-centric rebuild and so returned nothing.
/// </summary>
public record GetExecutiveOverviewQuery(Guid? ProjectId = null, string? Phase = null) : IRequest<ExecutiveOverviewDto>;

public class GetExecutiveOverviewQueryHandler : IRequestHandler<GetExecutiveOverviewQuery, ExecutiveOverviewDto>
{
    private readonly ExecutiveOverviewLoader _loader;

    public GetExecutiveOverviewQueryHandler(IApplicationDbContext db, IDynamicDataRepository dynamicDataRepository)
    {
        _loader = new ExecutiveOverviewLoader(db, dynamicDataRepository);
    }

    public async Task<ExecutiveOverviewDto> Handle(GetExecutiveOverviewQuery request, CancellationToken cancellationToken)
    {
        var inputs = await _loader.LoadAsync(withLabels: false, cancellationToken);
        var today = ExecutiveOverviewLoader.SaudiToday(DateTime.UtcNow);
        var phaseFilter = OverviewProgressPlan.Phases.Contains(request.Phase) ? request.Phase : null;
        return ExecutiveOverviewCalculator.Calculate(
            inputs.Projects, inputs.UnitCounts, inputs.Reports, inputs.Stock, today, request.ProjectId, phaseFilter);
    }
}
