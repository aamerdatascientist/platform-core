using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Platform.Application.Analytics.Queries.GetExecutiveOverview;

namespace Platform.Api.Controllers;

/// <summary>
/// The Executive Overview for people who are not signed in: the same data as
/// GET /api/analytics/executive-overview, opened by the share key instead of a login.
///
/// This is the only anonymous endpoint that returns business data, so it is deliberately
/// narrow - one read-only query, nothing else reachable - and it answers 404 (not 401/403)
/// when the key is missing, wrong, or not configured, so the address gives nothing away
/// about whether public viewing is even switched on. See <see cref="DashboardShareKey"/>.
/// </summary>
[ApiController]
[Route("api/public/executive-overview")]
[AllowAnonymous]
public class PublicDashboardController : ControllerBase
{
    private readonly ISender _sender;
    private readonly IConfiguration _configuration;

    public PublicDashboardController(ISender sender, IConfiguration configuration)
    {
        _sender = sender;
        _configuration = configuration;
    }

    [HttpGet]
    public async Task<ActionResult<ExecutiveOverviewDto>> Get(
        [FromQuery] Guid? projectId,
        [FromHeader(Name = DashboardShareKey.HeaderName)] string? key,
        CancellationToken cancellationToken)
    {
        if (!DashboardShareKey.Matches(_configuration, key)) return NotFound();

        // Shared screens poll this; nothing along the way should hand back a stale copy.
        Response.Headers.CacheControl = "no-store";
        return Ok(await _sender.Send(new GetExecutiveOverviewQuery(projectId), cancellationToken));
    }
}
