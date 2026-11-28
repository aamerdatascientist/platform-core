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
/// makes one without Claude, for free. Kind <c>custom</c> covers <paramref name="From"/> to
/// <paramref name="To"/> (both required); the other kinds work out their own start and take no From.
/// </summary>
public record RunReportCommand(string Kind, DateOnly? To, bool WithAnalysis, bool Deliver, DateOnly? From = null) : IRequest<ReportRunDto>;

public class RunReportCommandValidator : AbstractValidator<RunReportCommand>
{
    public RunReportCommandValidator()
    {
        RuleFor(x => x.Kind).Must(k => ReportRun.ManualKinds.Contains(k)).WithMessage("Kind must be daily, weekly, monthly or custom.");
        When(x => x.Kind == ReportRun.Custom, () =>
        {
            RuleFor(x => x.From).NotNull().WithMessage("Choose the first day of the report.");
            RuleFor(x => x.To).NotNull().WithMessage("Choose the last day of the report.");
            RuleFor(x => x).Must(x => x.From <= x.To).When(x => x.From is not null && x.To is not null)
                .WithName("From").WithMessage("The first day must be on or before the last day.");
            RuleFor(x => x).Must(x => x.To!.Value.DayNumber - x.From!.Value.DayNumber < ReportRun.MaxCustomDays)
                .When(x => x.From is not null && x.To is not null && x.From <= x.To)
                .WithName("From").WithMessage($"A report can cover at most {ReportRun.MaxCustomDays} days.");
            RuleFor(x => x.To).Must(to => to <= ExecutiveOverviewLoader.SaudiToday(DateTime.UtcNow))
                .When(x => x.To is not null).WithMessage("The last day can't be in the future.");
        });
        RuleFor(x => x.From).Null().When(x => x.Kind != ReportRun.Custom)
            .WithMessage("Only a custom report takes a first day.");
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
            request.WithAnalysis, request.Deliver, cancellationToken, request.Kind == ReportRun.Custom ? request.From : null);
        var run = result!.Run;
        return new ReportRunDto(
            run.Id, run.Kind, run.PeriodStart, run.PeriodEnd, run.Trigger, run.Status, run.Analysis, run.InputTokens,
            run.OutputTokens, run.CostUsd, run.Error, run.CreatedAtUtc, run.CompletedAtUtc, run.PdfBlobName is not null,
            Array.Empty<ReportDeliveryDto>());
    }
}
