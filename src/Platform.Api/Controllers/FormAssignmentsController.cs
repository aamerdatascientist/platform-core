using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Platform.Application.Forms.Assignments;
using Platform.Application.Forms.Assignments.Commands.CreateFormAssignments;
using Platform.Application.Forms.Assignments.Commands.StopFormAssignment;
using Platform.Application.Forms.Assignments.Queries.GetAssignmentDays;
using Platform.Application.Forms.Assignments.Queries.GetFormAssignments;
using Platform.Application.Forms.Assignments.Queries.GetMyAssignments;

namespace Platform.Api.Controllers;

/// <summary>
/// Daily form assignments: administrators assign users a form to fill every working day and
/// track who filled it and when; every signed-in user can read their own.
/// </summary>
[ApiController]
[Route("api/form-assignments")]
[Authorize]
public class FormAssignmentsController : ControllerBase
{
    private readonly ISender _sender;

    public FormAssignmentsController(ISender sender) => _sender = sender;

    [HttpGet("mine")]
    public async Task<ActionResult<MyAssignmentsDto>> Mine(CancellationToken cancellationToken) =>
        Ok(await _sender.Send(new GetMyAssignmentsQuery(), cancellationToken));

    [HttpGet]
    [Authorize(Roles = "Administrator")]
    public async Task<ActionResult<IReadOnlyList<FormAssignmentDto>>> List(CancellationToken cancellationToken) =>
        Ok(await _sender.Send(new GetFormAssignmentsQuery(), cancellationToken));

    [HttpGet("days")]
    [Authorize(Roles = "Administrator")]
    public async Task<ActionResult<IReadOnlyList<AssignmentDayDto>>> Days(
        [FromQuery] DateOnly from, [FromQuery] DateOnly to, [FromQuery] Guid? userId, [FromQuery] Guid? formId,
        CancellationToken cancellationToken) =>
        Ok(await _sender.Send(new GetAssignmentDaysQuery(from, to, userId, formId), cancellationToken));

    public record CreateAssignmentsRequest(
        Guid FormDefinitionId, IReadOnlyList<Guid> UserIds, DateOnly StartDate, DateOnly? EndDate, string? Note);

    [HttpPost]
    [Authorize(Roles = "Administrator")]
    public async Task<ActionResult<CreateFormAssignmentsResult>> Create(
        CreateAssignmentsRequest request, CancellationToken cancellationToken) =>
        Ok(await _sender.Send(
            new CreateFormAssignmentsCommand(request.FormDefinitionId, request.UserIds, request.StartDate, request.EndDate, request.Note),
            cancellationToken));

    [HttpPost("{id:guid}/stop")]
    [Authorize(Roles = "Administrator")]
    public async Task<IActionResult> Stop(Guid id, CancellationToken cancellationToken)
    {
        await _sender.Send(new StopFormAssignmentCommand(id), cancellationToken);
        return NoContent();
    }
}
