using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Platform.Application.Common.Interfaces;
using Platform.Application.Forms.Commands.SubmitFormData;
using Platform.Application.Forms.Queries.GetFormSubmissions;

namespace Platform.Api.Controllers;

[ApiController]
[Route("api/forms/{formId:guid}/submissions")]
[Authorize]
public class FormDataController : ControllerBase
{
    private readonly ISender _sender;
    private readonly ICurrentUserService _currentUser;

    public FormDataController(ISender sender, ICurrentUserService currentUser)
    {
        _sender = sender;
        _currentUser = currentUser;
    }

    [HttpPost]
    public async Task<IActionResult> Submit(
        Guid formId, [FromBody] Dictionary<string, object?> values, CancellationToken cancellationToken)
    {
        // SubmittedByUserId always comes from the authenticated principal, never the
        // request body - a client claiming to submit "as" someone else is not a thing
        // this endpoint will ever trust.
        var userId = _currentUser.UserId!.Value;

        var id = await _sender.Send(new SubmitFormDataCommand(formId, values, userId), cancellationToken);
        return Ok(new { id });
    }

    /// <summary>
    /// Every submitted record of a form - administrators only (2026-10-20). Other users fill
    /// forms in but don't browse what everyone else submitted; the form's own Lookup
    /// dropdowns get their choices from GET /api/forms/{id}/fields/{fieldId}/lookup-options
    /// instead, so locking this down doesn't empty them.
    /// </summary>
    [HttpGet]
    [Authorize(Roles = "Administrator")]
    public async Task<ActionResult<PagedResult<DynamicRow>>> List(
        Guid formId, [FromQuery] int page = 1, [FromQuery] int pageSize = 25,
        [FromQuery] string? filterFieldCode = null, [FromQuery] string? filterValue = null,
        CancellationToken cancellationToken = default) =>
        Ok(await _sender.Send(new GetFormSubmissionsQuery(formId, page, pageSize, filterFieldCode, filterValue), cancellationToken));
}
