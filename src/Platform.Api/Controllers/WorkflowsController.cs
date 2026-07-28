using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Platform.Application.Workflow.Commands.AddWorkflowState;
using Platform.Application.Workflow.Commands.AddWorkflowTransition;
using Platform.Application.Workflow.Commands.CreateWorkflowDefinition;
using Platform.Application.Workflow.Commands.PublishWorkflowDefinition;
using Platform.Application.Workflow.Dtos;
using Platform.Application.Workflow.Queries.GetWorkflowDefinition;

namespace Platform.Api.Controllers;

[ApiController]
[Route("api/workflows")]
[Authorize]
public class WorkflowsController : ControllerBase
{
    private readonly ISender _sender;

    public WorkflowsController(ISender sender) => _sender = sender;

    public record CreateWorkflowRequest(string Code, string Name, Guid FormDefinitionId);

    [HttpPost]
    public async Task<IActionResult> Create(CreateWorkflowRequest request, CancellationToken cancellationToken)
    {
        var id = await _sender.Send(new CreateWorkflowDefinitionCommand(request.Code, request.Name, request.FormDefinitionId), cancellationToken);
        return CreatedAtAction(nameof(Get), new { id }, new { id });
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<WorkflowDefinitionDto>> Get(Guid id, CancellationToken cancellationToken) =>
        Ok(await _sender.Send(new GetWorkflowDefinitionQuery(id), cancellationToken));

    public record AddStateRequest(string Code, string Label, bool IsInitial, bool IsFinal);

    [HttpPost("{id:guid}/states")]
    public async Task<IActionResult> AddState(Guid id, AddStateRequest request, CancellationToken cancellationToken)
    {
        var stateId = await _sender.Send(
            new AddWorkflowStateCommand(id, request.Code, request.Label, request.IsInitial, request.IsFinal), cancellationToken);
        return Ok(new { id = stateId });
    }

    public record AddTransitionRequest(string Code, string Label, Guid FromStateId, Guid ToStateId, IReadOnlyList<Guid> AllowedRoleIds);

    [HttpPost("{id:guid}/transitions")]
    public async Task<IActionResult> AddTransition(Guid id, AddTransitionRequest request, CancellationToken cancellationToken)
    {
        var transitionId = await _sender.Send(new AddWorkflowTransitionCommand(
            id, request.Code, request.Label, request.FromStateId, request.ToStateId, request.AllowedRoleIds), cancellationToken);
        return Ok(new { id = transitionId });
    }

    [HttpPost("{id:guid}/publish")]
    public async Task<IActionResult> Publish(Guid id, CancellationToken cancellationToken)
    {
        await _sender.Send(new PublishWorkflowDefinitionCommand(id), cancellationToken);
        return NoContent();
    }
}
