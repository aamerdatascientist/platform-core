using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Platform.Application.Identity.Commands.SetUserActiveStatus;
using Platform.Application.Identity.Commands.SetUserRoles;
using Platform.Application.Identity.Queries.GetUsers;

namespace Platform.Api.Controllers;

[ApiController]
[Route("api/users")]
[Authorize(Roles = "Administrator")]
public class UsersController : ControllerBase
{
    private readonly ISender _sender;

    public UsersController(ISender sender) => _sender = sender;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<UserSummaryDto>>> List(CancellationToken cancellationToken) =>
        Ok(await _sender.Send(new GetUsersQuery(), cancellationToken));

    public record SetRolesRequest(IReadOnlyList<Guid> RoleIds);

    [HttpPut("{id:guid}/roles")]
    public async Task<IActionResult> SetRoles(Guid id, SetRolesRequest request, CancellationToken cancellationToken)
    {
        await _sender.Send(new SetUserRolesCommand(id, request.RoleIds), cancellationToken);
        return NoContent();
    }

    public record SetActiveStatusRequest(bool IsActive);

    [HttpPut("{id:guid}/active-status")]
    public async Task<IActionResult> SetActiveStatus(Guid id, SetActiveStatusRequest request, CancellationToken cancellationToken)
    {
        await _sender.Send(new SetUserActiveStatusCommand(id, request.IsActive), cancellationToken);
        return NoContent();
    }
}
