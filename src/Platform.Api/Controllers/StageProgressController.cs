using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Platform.Application.Common.Interfaces;
using Platform.Application.StageProgress;

namespace Platform.Api.Controllers;

/// <summary>
/// Stage progress administration (2026-11-10): a project's progress by unit, reopening a
/// finished unit, a project's starting position, and re-running a project's setup.
/// Administrators only. Reports, plans and pauses themselves go through the forms.
/// </summary>
[ApiController]
[Route("api/stage-progress")]
[Authorize(Roles = "Administrator")]
public class StageProgressController(ISender sender, ICurrentUserService currentUser) : ControllerBase
{
    /// <summary>Every project, for the Stage progress chooser.</summary>
    [HttpGet("projects")]
    public async Task<ActionResult<IReadOnlyList<StageProjectSummaryDto>>> ListProjects(CancellationToken cancellationToken) =>
        Ok(await sender.Send(new ListStageProjectsQuery(), cancellationToken));

    [HttpGet("projects/{projectId:guid}")]
    public async Task<ActionResult<ProjectProgressDto>> GetProgress(Guid projectId, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetProjectProgressQuery(projectId), cancellationToken));

    public record ReopenRequest(string StageCode, string? SubstepCode, Guid? LevelId, int? Side, string? Reason);

    [HttpPost("projects/{projectId:guid}/reopen")]
    public async Task<IActionResult> Reopen(Guid projectId, ReopenRequest request, CancellationToken cancellationToken)
    {
        await sender.Send(new ReopenStageUnitCommand(projectId, request.StageCode, request.SubstepCode, request.LevelId, request.Side, request.Reason), cancellationToken);
        return NoContent();
    }

    [HttpPut("projects/{projectId:guid}/baseline")]
    public async Task<IActionResult> SetBaseline(Guid projectId, IReadOnlyList<StageBaselineInput> entries, CancellationToken cancellationToken) =>
        Ok(new { count = await sender.Send(new SetStageBaselineCommand(projectId, entries), cancellationToken) });

    [HttpPost("projects/{projectId:guid}/setup")]
    public async Task<IActionResult> EnsureSetup(Guid projectId, CancellationToken cancellationToken)
    {
        await sender.Send(new EnsureProjectSetupCommand(projectId, currentUser.UserId ?? Guid.Empty), cancellationToken);
        return NoContent();
    }
}
