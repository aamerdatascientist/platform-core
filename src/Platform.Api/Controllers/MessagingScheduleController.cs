using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Platform.Application.Notifications;

namespace Platform.Api.Controllers;

/// <summary>
/// What the app sends on its own and when (administrators, the Messages page): form reminders,
/// the "you've been assigned" message, and the daily/weekly/monthly stakeholder reports.
/// </summary>
[ApiController]
[Route("api/messaging-schedule")]
[Authorize(Roles = "Administrator")]
public class MessagingScheduleController(ISender sender) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<MessagingScheduleDto>> Get(CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetMessagingScheduleQuery(), cancellationToken));

    [HttpPut]
    public async Task<ActionResult<MessagingScheduleDto>> Save(MessagingScheduleDto schedule, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new SaveMessagingScheduleCommand(schedule), cancellationToken));
}
