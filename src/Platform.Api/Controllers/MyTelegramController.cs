using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Platform.Application.Common.Exceptions;
using Platform.Application.Common.Interfaces;
using Platform.Application.Identity.Commands.CreateTelegramLink;
using Platform.Application.Identity.Commands.UnlinkTelegram;
using Platform.Application.Identity.Queries.GetTelegramStatus;

namespace Platform.Api.Controllers;

/// <summary>The signed-in user's own Telegram connection (Settings page) - any role.</summary>
[ApiController]
[Route("api/me/telegram")]
[Authorize]
public class MyTelegramController : ControllerBase
{
    private readonly ISender _sender;
    private readonly ICurrentUserService _currentUser;

    public MyTelegramController(ISender sender, ICurrentUserService currentUser)
    {
        _sender = sender;
        _currentUser = currentUser;
    }

    private Guid UserId => _currentUser.UserId ?? throw new UnauthorizedException("Not signed in.");

    [HttpGet]
    public async Task<ActionResult<TelegramStatusDto>> Status(CancellationToken cancellationToken) =>
        Ok(await _sender.Send(new GetTelegramStatusQuery(UserId), cancellationToken));

    [HttpPost("link")]
    public async Task<ActionResult<TelegramLinkDto>> Link(CancellationToken cancellationToken) =>
        Ok(await _sender.Send(new CreateTelegramLinkCommand(UserId), cancellationToken));

    [HttpDelete]
    public async Task<IActionResult> Unlink(CancellationToken cancellationToken)
    {
        await _sender.Send(new UnlinkTelegramCommand(UserId), cancellationToken);
        return NoContent();
    }
}
