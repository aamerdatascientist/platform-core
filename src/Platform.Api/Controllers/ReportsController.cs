using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Platform.Application.Reports;
using Platform.Application.Reports.Commands.DeleteReportRecipient;
using Platform.Application.Reports.Commands.RunReport;
using Platform.Application.Reports.Commands.SaveReportRecipient;
using Platform.Application.Reports.Queries.GetReportDownload;
using Platform.Application.Reports.Queries.GetReportsOverview;

namespace Platform.Api.Controllers;

/// <summary>
/// Stakeholder reports (administrators): who receives them, this month's Claude spend against
/// the cap, the history, a preview or an immediate send, and downloads.
/// </summary>
[ApiController]
[Route("api/reports")]
[Authorize(Roles = "Administrator")]
public class ReportsController : ControllerBase
{
    private readonly ISender _sender;

    public ReportsController(ISender sender) => _sender = sender;

    [HttpGet]
    public async Task<ActionResult<ReportsOverviewDto>> Overview(CancellationToken cancellationToken) =>
        Ok(await _sender.Send(new GetReportsOverviewQuery(), cancellationToken));

    public record RecipientRequest(string Name, string PhoneNumber, bool ReceivesDaily, bool ReceivesWeekly, bool ReceivesMonthly, bool IsActive = true);

    [HttpPost("recipients")]
    public async Task<ActionResult<ReportRecipientDto>> AddRecipient(RecipientRequest request, CancellationToken cancellationToken) =>
        Ok(await _sender.Send(new SaveReportRecipientCommand(
            null, request.Name, request.PhoneNumber, request.ReceivesDaily, request.ReceivesWeekly, request.ReceivesMonthly, true), cancellationToken));

    [HttpPut("recipients/{id:guid}")]
    public async Task<ActionResult<ReportRecipientDto>> UpdateRecipient(Guid id, RecipientRequest request, CancellationToken cancellationToken) =>
        Ok(await _sender.Send(new SaveReportRecipientCommand(
            id, request.Name, request.PhoneNumber, request.ReceivesDaily, request.ReceivesWeekly, request.ReceivesMonthly, request.IsActive), cancellationToken));

    [HttpDelete("recipients/{id:guid}")]
    public async Task<IActionResult> DeleteRecipient(Guid id, CancellationToken cancellationToken)
    {
        await _sender.Send(new DeleteReportRecipientCommand(id), cancellationToken);
        return NoContent();
    }

    /// <param name="Deliver">False = preview (stored, downloadable, not sent).</param>
    public record RunRequest(string Kind, DateOnly? To, bool WithAnalysis, bool Deliver);

    /// <summary>Makes a report now. Takes up to a couple of minutes with the analysis.</summary>
    [HttpPost("run")]
    [EnableRateLimiting(RateLimitPolicies.Reports)]
    public async Task<ActionResult<ReportRunDto>> Run(RunRequest request, CancellationToken cancellationToken) =>
        Ok(await _sender.Send(new RunReportCommand(request.Kind, request.To, request.WithAnalysis, request.Deliver), cancellationToken));

    public record DownloadDto(string Url);

    [HttpGet("runs/{id:guid}/download")]
    public async Task<ActionResult<DownloadDto>> Download(Guid id, [FromQuery] bool phone, CancellationToken cancellationToken) =>
        Ok(new DownloadDto(await _sender.Send(new GetReportDownloadQuery(id, phone), cancellationToken)));
}
