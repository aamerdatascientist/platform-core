using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Platform.Application.Identity.Commands.CreateRole;
using Platform.Application.Identity.Queries.GetRoles;

namespace Platform.Api.Controllers;

[ApiController]
[Route("api/roles")]
[Authorize(Roles = "Administrator")]
public class RolesController : ControllerBase
{
    private readonly ISender _sender;

    public RolesController(ISender sender) => _sender = sender;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<RoleDto>>> List(CancellationToken cancellationToken) =>
        Ok(await _sender.Send(new GetRolesQuery(), cancellationToken));

    public record CreateRoleRequest(string Name, string? Description);

    [HttpPost]
    public async Task<IActionResult> Create(CreateRoleRequest request, CancellationToken cancellationToken)
    {
        var id = await _sender.Send(new CreateRoleCommand(request.Name, request.Description), cancellationToken);
        return Ok(new { id });
    }
}
