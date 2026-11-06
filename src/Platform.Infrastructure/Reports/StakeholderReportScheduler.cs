using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Platform.Application.Common.Interfaces;
using Platform.Application.Reports;
using Platform.Domain.Reports;

namespace Platform.Infrastructure.Reports;

/// <summary>
/// Sends the stakeholder reports at the send time (Reports:SendTime, default 18:00 Saudi):
/// the daily report on working days (Saturday-Thursday), the weekly one on Thursday (end of
/// the Saudi working week) and the monthly one on the last day of the month (owner,
/// 2026-11-06). Checked once a minute; a report missed by up to <see cref="LateWindow"/>
/// (a restart at 6 pm) still goes out. A report is only made when someone receives that kind,
/// so nothing is spent on reports nobody gets, and never twice (see ReportRun).
/// </summary>
public class StakeholderReportScheduler : BackgroundService
{
    private static readonly TimeSpan LateWindow = TimeSpan.FromHours(3);
    private static readonly TimeSpan SaudiArabiaOffset = TimeSpan.FromHours(3);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ReportSettings _settings;
    private readonly ILogger<StakeholderReportScheduler> _logger;

    public StakeholderReportScheduler(IServiceScopeFactory scopeFactory, ReportSettings settings, ILogger<StakeholderReportScheduler> logger)
    {
        _scopeFactory = scopeFactory;
        _settings = settings;
        _logger = logger;
    }

    /// <summary>Which reports are due on a Saudi calendar day.</summary>
    public static IReadOnlyList<string> DueKinds(DateOnly day)
    {
        var kinds = new List<string>();
        if (StakeholderReportBuilder.IsWorkingDay(day)) kinds.Add(ReportRun.Daily);
        if (day.DayOfWeek == DayOfWeek.Thursday) kinds.Add(ReportRun.Weekly);
        if (day.AddDays(1).Month != day.Month) kinds.Add(ReportRun.Monthly);
        return kinds;
    }

    /// <summary>True from the send time until <see cref="LateWindow"/> after it.</summary>
    public static bool IsSendWindow(string sendTime, TimeOnly saudiNow)
    {
        if (!TimeOnly.TryParseExact(sendTime, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var at)) return false;
        return saudiNow >= at && saudiNow.ToTimeSpan() - at.ToTimeSpan() < LateWindow;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_settings.IsPdfConfigured)
        {
            _logger.LogInformation("The PDF service isn't configured (Reports:PdfServiceUrl) - stakeholder reports are off.");
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        do
        {
            try
            {
                var saudiNow = DateTime.UtcNow + SaudiArabiaOffset;
                if (IsSendWindow(_settings.SendTime, TimeOnly.FromDateTime(saudiNow)))
                    await SendDueAsync(DateOnly.FromDateTime(saudiNow), stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Stakeholder report scheduling failed; trying again next minute.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task SendDueAsync(DateOnly today, CancellationToken cancellationToken)
    {
        foreach (var kind in DueKinds(today))
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
            var (from, _, _, _) = StakeholderReportBuilder.Period(kind, today);

            // Cheap checks first, every minute of the window: already made, or nobody to send to.
            if (await db.ReportRuns.AnyAsync(r => r.Trigger == ReportRun.Schedule && r.Kind == kind && r.PeriodStart == from, cancellationToken))
                continue;
            var recipients = await db.ReportRecipients.ToListAsync(cancellationToken);
            if (!recipients.Any(r => r.Receives(kind))) continue;

            var service = scope.ServiceProvider.GetRequiredService<StakeholderReportService>();
            var result = await service.RunAsync(kind, today, ReportRun.Schedule, null, withAnalysis: true, deliver: true, cancellationToken);
            if (result is not null)
                _logger.LogInformation("Stakeholder report {Kind} for {Day}: {Status}, analysis {Analysis}, ${Cost}.",
                    kind, today, result.Run.Status, result.Run.Analysis, result.Run.CostUsd);
        }
    }
}
