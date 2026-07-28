using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Platform.Application.Workflow.Commands.ExecuteWorkflowTransition;
using Platform.Application.Workflow.Dtos;
using Platform.Application.Workflow.Queries.GetWorkflowStatus;

namespace Platform.Api.Controllers;

/// <summary>
/// Keyed by RecordId rather than nested under /forms/{formId} - a record's workflow
/// status is meaningful on its own once you have the record, and RecordId alone is
/// enough to resolve it unambiguously (see WorkflowInstance remarks).
/// </summary>
[ApiController]
[Route("api/records/{recordId:guid}/workflow")]
[Authorize]
public class RecordWorkflowController : ControllerBase
{
    private readonly ISender _sender;

    public RecordWorkflowController(ISender sender) => _sender = sender;

    [HttpGet]
    public async Task<ActionResult<WorkflowStatusDto>> GetStatus(Guid recordId, CancellationToken cancellationToken) =>
        Ok(await _sender.Send(new GetWorkflowStatusQuery(recordId), cancellationToken));

    public record ExecuteTransitionRequest(string TransitionCode, string? Comment);

    [HttpPost("transitions")]
    public async Task<IActionResult> ExecuteTransition(Guid recordId, ExecuteTransitionRequest request, CancellationToken cancellationToken)
    {
        await _sender.Send(new ExecuteWorkflowTransitionCommand(recordId, request.TransitionCode, request.Comment), cancellationToken);
        return NoContent();
    }
}
