using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Platform.Application.Analytics.Queries.GetCrewCountByProject;
using Platform.Application.Analytics.Queries.GetExecutiveOverview;
using Platform.Application.Analytics.Queries.GetOverdueTasks;
using Platform.Application.Analytics.Queries.GetProjectProgress;
using Platform.Application.Analytics.Queries.GetStockMovementBreakdown;
using Platform.Application.Analytics.Queries.GetWeatherImpactedDays;

namespace Platform.Api.Controllers;

/// <summary>
/// Read-only dashboard aggregate endpoints. No role-gating, deliberately - unlike form
/// access (FormAccessChecker), nothing about these KPIs is scoped per-user today; every
/// authenticated user sees the same Executive Overview.
/// </summary>
[ApiController]
[Route("api/analytics")]
[Authorize]
public class AnalyticsController : ControllerBase
{
    private readonly ISender _sender;

    public AnalyticsController(ISender sender) => _sender = sender;

    /// <summary>
    /// Everything the Executive Overview page shows, in one response, read from the seven
    /// forms that exist today. The five endpoints below predate the projects-centric rebuild
    /// and read forms that no longer exist - the page no longer calls them.
    /// </summary>
    [HttpGet("executive-overview")]
    public async Task<ActionResult<ExecutiveOverviewDto>> GetExecutiveOverview(
        [FromQuery] Guid? projectId, CancellationToken cancellationToken) =>
        Ok(await _sender.Send(new GetExecutiveOverviewQuery(projectId), cancellationToken));

    [HttpGet("project-progress")]
    public async Task<ActionResult<IReadOnlyList<ProjectProgressDto>>> GetProjectProgress(CancellationToken cancellationToken) =>
        Ok(await _sender.Send(new GetProjectProgressQuery(), cancellationToken));

    [HttpGet("crew-count-by-project")]
    public async Task<ActionResult<IReadOnlyList<ProjectCrewCountDto>>> GetCrewCountByProject(CancellationToken cancellationToken) =>
        Ok(await _sender.Send(new GetCrewCountByProjectQuery(), cancellationToken));

    [HttpGet("weather-impacted-days")]
    public async Task<ActionResult<IReadOnlyList<ProjectWeatherSummaryDto>>> GetWeatherImpactedDays(CancellationToken cancellationToken) =>
        Ok(await _sender.Send(new GetWeatherImpactedDaysQuery(), cancellationToken));

    [HttpGet("overdue-tasks")]
    public async Task<ActionResult<IReadOnlyList<OverdueTaskDto>>> GetOverdueTasks(CancellationToken cancellationToken) =>
        Ok(await _sender.Send(new GetOverdueTasksQuery(), cancellationToken));

    [HttpGet("stock-movement-breakdown")]
    public async Task<ActionResult<IReadOnlyList<StockMovementBreakdownDto>>> GetStockMovementBreakdown(CancellationToken cancellationToken) =>
        Ok(await _sender.Send(new GetStockMovementBreakdownQuery(), cancellationToken));
}
