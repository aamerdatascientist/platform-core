using FluentValidation;
using MediatR;
using Platform.Application.Common.Interfaces;
using Platform.Application.Analytics.Queries.GetExecutiveOverview;
using Platform.Domain.Reports;

namespace Platform.Application.Reports.Commands.RunReport;

/// <summary>
/// An administrator makes a report now: a preview (stored, not sent) or "send now" (sent to the
/// recipients of that kind). <paramref name="To"/> defaults to today (Saudi). Claude's cost counts
/// towards the monthly cap exactly like a scheduled report; <paramref name="WithAnalysis"/> false
/// makes one without Claude, for free.
/// </summary>
public record RunReportCommand(string Kind, DateOnly? To, bool WithAnalysis, bool Deliver) : IRequest<ReportRunDto>;

public class RunReportCommandValidator : AbstractValidator<RunReportCommand>
{
    public RunReportCommandValidator()
    {
        RuleFor(x => x.Kind).Must(k => ReportRun.Kinds.Contains(k)).WithMessage("Kind must be daily, weekly or monthly.");
    }
}

public class RunReportCommandHandler : IRequestHandler<RunReportCommand, ReportRunDto>
{
    private readonly StakeholderReportService _reports;
    private readonly ICurrentUserService _currentUser;

    public RunReportCommandHandler(StakeholderReportService reports, ICurrentUserService currentUser)
    {
        _reports = reports;
        _currentUser = currentUser;
    }

    public async Task<ReportRunDto> Handle(RunReportCommand request, CancellationToken cancellationToken)
    {
        var to = request.To ?? ExecutiveOverviewLoader.SaudiToday(DateTime.UtcNow);
        var result = await _reports.RunAsync(
            request.Kind, to, request.Deliver ? ReportRun.SendNow : ReportRun.Preview, _currentUser.UserId,
            request.WithAnalysis, request.Deliver, cancellationToken);
        var run = result!.Run;
        return new ReportRunDto(
            run.Id, run.Kind, run.PeriodStart, run.PeriodEnd, run.Trigger, run.Status, run.Analysis, run.InputTokens,
            run.OutputTokens, run.CostUsd, run.Error, run.CreatedAtUtc, run.CompletedAtUtc, run.PdfBlobName is not null,
            Array.Empty<ReportDeliveryDto>());
    }
}
