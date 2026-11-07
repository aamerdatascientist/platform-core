using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Platform.Application.Forms.Records;

namespace Platform.Api.Controllers;

/// <summary>
/// My entries (redesign §9.3): what the signed-in person sent, across the forms they can open.
/// Every role may call it - it only ever returns the caller's own records.
/// </summary>
[ApiController]
[Route("api/submissions")]
[Authorize]
public class MySubmissionsController : ControllerBase
{
    private readonly ISender _sender;
    public MySubmissionsController(ISender sender) => _sender = sender;

    [HttpGet("mine")]
    public async Task<ActionResult<MyEntriesPageDto>> Mine(
        [FromQuery] Guid? formId = null, [FromQuery] string? before = null, [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default) =>
        Ok(await _sender.Send(new GetMyEntriesQuery(formId, before, pageSize), cancellationToken));
}
