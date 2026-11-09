using MediatR;
using Microsoft.EntityFrameworkCore;
using Platform.Application.Common.Interfaces;

namespace Platform.Application.Reports.Queries.GetReportsOverview;

/// <summary>The Reports admin page: setup status, this month's Claude spend against the cap, recipients and recent reports.</summary>
public record GetReportsOverviewQuery : IRequest<ReportsOverviewDto>;

public class GetReportsOverviewQueryHandler : IRequestHandler<GetReportsOverviewQuery, ReportsOverviewDto>
{
    private const int RecentRuns = 40;

    private readonly IApplicationDbContext _db;
    private readonly StakeholderReportService _reports;
    private readonly ReportSettings _settings;
    private readonly WhatsAppSettings _whatsApp;

    public GetReportsOverviewQueryHandler(
        IApplicationDbContext db, StakeholderReportService reports, ReportSettings settings, WhatsAppSettings whatsApp)
    {
        _db = db;
        _reports = reports;
        _settings = settings;
        _whatsApp = whatsApp;
    }

    public async Task<ReportsOverviewDto> Handle(GetReportsOverviewQuery request, CancellationToken cancellationToken)
    {
        var recipients = await _db.ReportRecipients.OrderBy(r => r.Name).ToListAsync(cancellationToken);
        var runs = await _db.ReportRuns.OrderByDescending(r => r.CreatedAtUtc).Take(RecentRuns).ToListAsync(cancellationToken);
        var runIds = runs.Select(r => r.Id).ToList();
        var deliveries = await _db.ReportDeliveries.Where(d => runIds.Contains(d.ReportRunId)).ToListAsync(cancellationToken);
        // Names even for recipients removed since.
        var names = await _db.ReportRecipients.IgnoreQueryFilters().ToDictionaryAsync(r => r.Id, r => r.Name, cancellationToken);

        return new ReportsOverviewDto(
            _settings.IsAnalysisConfigured, _settings.IsPdfConfigured, _whatsApp.IsConfigured, _settings.Model, _settings.SendTime,
            _settings.MonthlyBudgetUsd, await _reports.SpentThisMonthAsync(DateTime.UtcNow, cancellationToken),
            recipients.Select(ReportRecipientDto.From).ToList(),
            runs.Select(run => new ReportRunDto(
                run.Id, run.Kind, run.PeriodStart, run.PeriodEnd, run.Trigger, run.Status, run.Analysis, run.InputTokens,
                run.OutputTokens, run.CostUsd, run.Error, run.CreatedAtUtc, run.CompletedAtUtc, run.PdfBlobName is not null,
                deliveries.Where(d => d.ReportRunId == run.Id)
                    .Select(d => new ReportDeliveryDto(names.GetValueOrDefault(d.RecipientId) ?? "", d.PhoneNumber, d.Status, d.Error, d.SentAtUtc))
                    .ToList()))
                .ToList());
    }
}
