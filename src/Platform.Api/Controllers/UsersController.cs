using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Platform.Application.Identity.Commands.ResetUserPassword;
using Platform.Application.Identity.Commands.SetUserActiveStatus;
using Platform.Application.Identity.Commands.SetUserPhoneNumber;
using Platform.Application.Notifications;
using Platform.Application.Identity.Commands.CreateTelegramLink;
using Platform.Application.Identity.Commands.UnlinkTelegram;
using Platform.Application.Identity.Commands.SetUserRoles;
using Platform.Application.Common.Interfaces;
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

    public record ResetPasswordRequest(string NewPassword);

    /// <summary>Sets a new password for a user who forgot theirs, and signs them out
    /// everywhere.</summary>
    [HttpPost("{id:guid}/password")]
    public async Task<IActionResult> ResetPassword(Guid id, ResetPasswordRequest request, CancellationToken cancellationToken)
    {
        await _sender.Send(new ResetUserPasswordCommand(id, request.NewPassword), cancellationToken);
        return NoContent();
    }

    public record SetPhoneNumberRequest(string? PhoneNumber);

    /// <summary>The user's mobile number for WhatsApp notifications; blank removes it.</summary>
    [HttpPut("{id:guid}/phone")]
    public async Task<IActionResult> SetPhoneNumber(Guid id, SetPhoneNumberRequest request, CancellationToken cancellationToken)
    {
        await _sender.Send(new SetUserPhoneNumberCommand(id, request.PhoneNumber), cancellationToken);
        return NoContent();
    }

    /// <summary>Sends the user a sample WhatsApp reminder and reports what Meta answered.</summary>
    [HttpPost("{id:guid}/whatsapp-test")]
    public async Task<ActionResult<NotificationSendResult>> SendWhatsAppTest(
        Guid id, [FromServices] AssignmentNotifier notifier, CancellationToken cancellationToken) =>
        Ok(await notifier.SendTestAsync(id, AssignmentNotifier.WhatsAppChannel, cancellationToken));

    /// <summary>A "Connect Telegram" link for this user, for an administrator to send them.</summary>
    [HttpPost("{id:guid}/telegram-link")]
    public async Task<ActionResult<TelegramLinkDto>> CreateTelegramLink(Guid id, CancellationToken cancellationToken) =>
        Ok(await _sender.Send(new CreateTelegramLinkCommand(id), cancellationToken));

    [HttpDelete("{id:guid}/telegram")]
    public async Task<IActionResult> UnlinkTelegram(Guid id, CancellationToken cancellationToken)
    {
        await _sender.Send(new UnlinkTelegramCommand(id), cancellationToken);
        return NoContent();
    }

    /// <summary>Sends the user a sample Telegram message and reports what Telegram answered.</summary>
    [HttpPost("{id:guid}/telegram-test")]
    public async Task<ActionResult<NotificationSendResult>> SendTelegramTest(
        Guid id, [FromServices] AssignmentNotifier notifier, CancellationToken cancellationToken) =>
        Ok(await notifier.SendTestAsync(id, AssignmentNotifier.TelegramChannel, cancellationToken));

    public record SetActiveStatusRequest(bool IsActive);

    [HttpPut("{id:guid}/active-status")]
    public async Task<IActionResult> SetActiveStatus(Guid id, SetActiveStatusRequest request, CancellationToken cancellationToken)
    {
        await _sender.Send(new SetUserActiveStatusCommand(id, request.IsActive), cancellationToken);
        return NoContent();
    }
}
