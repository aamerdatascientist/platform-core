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
/// Read-only dashboard aggregate endpoints - administrators only (2026-10-20: the dashboard
/// is for management, not for site users such as project managers). The KPIs aren't scoped
/// per user, so the only gate is the role. Viewing without a sign-in goes through the
/// separate share-key endpoint, PublicDashboardController.
/// </summary>
[ApiController]
[Route("api/analytics")]
[Authorize(Roles = "Administrator")]
public class AnalyticsController : ControllerBase
{
    private readonly ISender _sender;
    private readonly IConfiguration _configuration;

    public AnalyticsController(ISender sender, IConfiguration configuration)
    {
        _sender = sender;
        _configuration = configuration;
    }

    /// <summary>
    /// Everything the Executive Overview page shows, in one response, read from the seven
    /// forms that exist today. The five endpoints below predate the projects-centric rebuild
    /// and read forms that no longer exist - the page no longer calls them.
    /// </summary>
    [HttpGet("executive-overview")]
    public async Task<ActionResult<ExecutiveOverviewDto>> GetExecutiveOverview(
        [FromQuery] Guid? projectId, CancellationToken cancellationToken) =>
        Ok(await _sender.Send(new GetExecutiveOverviewQuery(projectId), cancellationToken));

    public record DashboardShareKeyResponse(string? Key);

    /// <summary>
    /// The key that opens the dashboard without a sign-in (see <see cref="DashboardShareKey"/>),
    /// or null when public viewing is switched off. Administrators only, like the rest of this
    /// controller: the app opens the dashboard in its own tab using this key.
    /// </summary>
    [HttpGet("executive-overview/share-key")]
    public ActionResult<DashboardShareKeyResponse> GetExecutiveOverviewShareKey() =>
        Ok(new DashboardShareKeyResponse(DashboardShareKey.Read(_configuration)));

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
