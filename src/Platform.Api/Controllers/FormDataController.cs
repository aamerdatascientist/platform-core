using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Platform.Application.Common.Interfaces;
using Platform.Application.Forms.Commands.DeleteSubmission;
using Platform.Application.Forms.Commands.SubmitFormData;
using Platform.Application.Forms.Commands.UpdateSubmission;
using Platform.Application.Forms.Queries.GetFormSubmissions;

using Platform.Application.Forms.Records;

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

    /// <summary>Corrects a submitted record - administrators only. The body is the whole
    /// record, in the same shape as a new submission, and is validated the same way.</summary>
    [HttpPut("{recordId:guid}")]
    [Authorize(Roles = "Administrator")]
    public async Task<IActionResult> Update(
        Guid formId, Guid recordId, [FromBody] Dictionary<string, object?> values, CancellationToken cancellationToken)
    {
        await _sender.Send(
            new UpdateSubmissionCommand(formId, recordId, values, _currentUser.UserId!.Value), cancellationToken);
        return NoContent();
    }

    /// <summary>Removes a submitted record (soft delete) - administrators only.</summary>
    [HttpDelete("{recordId:guid}")]
    [Authorize(Roles = "Administrator")]
    public async Task<IActionResult> Delete(Guid formId, Guid recordId, CancellationToken cancellationToken)
    {
        await _sender.Send(new DeleteSubmissionCommand(formId, recordId, _currentUser.UserId!.Value), cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// Every submitted record of a form - administrators only (2026-10-20). Other users fill
    /// forms in but don't browse what everyone else submitted; the form's own Lookup
    /// dropdowns get their choices from GET /api/forms/{id}/fields/{fieldId}/lookup-options
    /// instead, so locking this down doesn't empty them.
    ///
    /// Redesign phase 7 (Records tab, HANDOFF §9.4): optional q (text), from/to (yyyy-mm-dd,
    /// Saudi days; on dateField when given, else on when it was sent) and repeatable
    /// f=code:value filters. Each item now also carries createdAtUtc, createdByName,
    /// lookupLabels and attachmentCount - a superset of the old {id, values}, so old callers
    /// keep working. The old filterFieldCode/filterValue pair still works too.
    /// </summary>
    [HttpGet]
    [Authorize(Roles = "Administrator")]
    public async Task<ActionResult<RecordsPageDto>> List(
        Guid formId, [FromQuery] int page = 1, [FromQuery] int pageSize = 25,
        [FromQuery] string? filterFieldCode = null, [FromQuery] string? filterValue = null,
        [FromQuery] string? q = null, [FromQuery] string? from = null, [FromQuery] string? to = null,
        [FromQuery] string? dateField = null, [FromQuery(Name = "f")] string[]? filters = null,
        CancellationToken cancellationToken = default)
    {
        var all = (filters ?? Array.Empty<string>()).ToList();
        if (filterFieldCode is not null || filterValue is not null)
        {
            if (string.IsNullOrEmpty(filterFieldCode) || string.IsNullOrEmpty(filterValue))
                return BadRequest(new { title = "filterFieldCode and filterValue go together." });
            all.Add($"{filterFieldCode}:{filterValue}");
        }
        return Ok(await _sender.Send(new SearchRecordsQuery(formId, page, pageSize, q, from, to, dateField, all), cancellationToken));
    }

    /// <summary>One record with its attachments - its sender or an administrator, and only
    /// with access to the form (redesign §9.3; owner decision 2026-11-06). 403 otherwise.</summary>
    [HttpGet("{recordId:guid}")]
    public async Task<ActionResult<RecordDetailDto>> Get(Guid formId, Guid recordId, CancellationToken cancellationToken) =>
        Ok(await _sender.Send(new GetRecordDetailQuery(formId, recordId), cancellationToken));
}
