using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Platform.Application.Common.Interfaces;
using Platform.Application.Identity.Commands.Login;
using Platform.Application.Identity.Commands.RegisterUser;
using Platform.Application.Identity.Queries.GetCurrentUser;

namespace Platform.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly ISender _sender;

    public AuthController(ISender sender) => _sender = sender;

    public record RegisterRequest(string Email, string DisplayName, string Password, Guid? DepartmentId, Guid DefaultRoleId);

    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterRequest request, CancellationToken cancellationToken)
    {
        var userId = await _sender.Send(
            new RegisterUserCommand(request.Email, request.DisplayName, request.Password, request.DepartmentId, request.DefaultRoleId),
            cancellationToken);

        return CreatedAtAction(nameof(Register), new { id = userId }, new { id = userId });
    }

    public record LoginRequest(string Email, string Password);

    [HttpPost("login")]
    public async Task<ActionResult<TokenPair>> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        var tokens = await _sender.Send(new LoginCommand(request.Email, request.Password), cancellationToken);
        return Ok(tokens);
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<CurrentUserDto>> Me(CancellationToken cancellationToken)
    {
        var user = await _sender.Send(new GetCurrentUserQuery(), cancellationToken);
        return Ok(user);
    }
}
