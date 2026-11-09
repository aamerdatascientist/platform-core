using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Platform.Application.Analytics.Queries.GetExecutiveOverview;
using Platform.Application.Common.Interfaces;
using Platform.Domain.Reports;

namespace Platform.Application.Reports;

/// <summary>Draws a report as an HTML page (A4 or phone-width).</summary>
public interface IReportRenderer
{
    /// <param name="analysisNote">Shown instead of the analysis when there is none (e.g. the budget was reached).</param>
    string RenderHtml(StakeholderReport report, ReportAnalysis? analysis, string? analysisNote, bool phone);
}

/// <summary>HTML to PDF (the Gotenberg service).</summary>
public interface IPdfRenderer
{
    /// <param name="phone">One long phone-width page instead of A4 pages.</param>
    Task<byte[]> RenderAsync(string html, bool phone, CancellationToken cancellationToken);
}

/// <summary>Sends a PDF on WhatsApp: upload it to Meta, then a template whose header is that document.</summary>
public interface IWhatsAppDocumentSender
{
    Task<NotificationSendResult> SendDocumentAsync(
        string to, string templateName, byte[] pdf, string fileName, IReadOnlyList<string> bodyParameters,
        CancellationToken cancellationToken = default);
}

public record ReportRunResult(ReportRun Run, bool AlreadyDone);

/// <summary>
/// Makes one report end to end: figures -> Claude's analysis (within the monthly budget, every
/// number checked) -> PDFs (A4 and phone) -> stored -> sent on WhatsApp to the recipients of
/// that kind. Every step's outcome is on the <see cref="ReportRun"/> row, and the Claude cost
/// is saved the moment Claude answers.
/// </summary>
public class StakeholderReportService
{
    private static readonly TimeSpan SaudiArabiaOffset = TimeSpan.FromHours(3);

    private readonly IApplicationDbContext _db;
    private readonly IDynamicDataRepository _dynamicData;
    private readonly IReportAnalyst _analyst;
    private readonly IReportRenderer _renderer;
    private readonly IPdfRenderer _pdf;
    private readonly IBlobStorageService _blobs;
    private readonly IWhatsAppDocumentSender _whatsApp;
    private readonly ReportSettings _settings;
    private readonly WhatsAppSettings _whatsAppSettings;
    private readonly ILogger<StakeholderReportService> _logger;

    public StakeholderReportService(
        IApplicationDbContext db, IDynamicDataRepository dynamicData, IReportAnalyst analyst, IReportRenderer renderer,
        IPdfRenderer pdf, IBlobStorageService blobs, IWhatsAppDocumentSender whatsApp, ReportSettings settings,
        WhatsAppSettings whatsAppSettings, ILogger<StakeholderReportService> logger)
    {
        _db = db;
        _dynamicData = dynamicData;
        _analyst = analyst;
        _renderer = renderer;
        _pdf = pdf;
        _blobs = blobs;
        _whatsApp = whatsApp;
        _settings = settings;
        _whatsAppSettings = whatsAppSettings;
        _logger = logger;
    }

    /// <summary>Claude spend so far in the current Saudi calendar month, US dollars.</summary>
    public async Task<decimal> SpentThisMonthAsync(DateTime utcNow, CancellationToken cancellationToken)
    {
        var start = MonthStartUtc(utcNow);
        var costs = await _db.ReportRuns.Where(r => r.CreatedAtUtc >= start).Select(r => r.CostUsd).ToListAsync(cancellationToken);
        return costs.Sum();
    }

    public static DateTime MonthStartUtc(DateTime utcNow)
    {
        var saudi = utcNow + SaudiArabiaOffset;
        return DateTime.SpecifyKind(new DateTime(saudi.Year, saudi.Month, 1) - SaudiArabiaOffset, DateTimeKind.Utc);
    }

    /// <summary>
    /// The most one Claude call can cost: the figures (at a generous 2 characters per token, plus
    /// the instructions) and a full-length answer. A call is only made if this still fits.
    /// </summary>
    public decimal WorstCaseCallCost(string figuresJson) =>
        _settings.Cost(figuresJson.Length / 2 + 4000, _settings.MaxOutputTokens);

    /// <param name="deliver">Send to the recipients (schedule / send now) or only store it (preview).</param>
    /// <returns>Null run when a scheduled report for this period was already claimed by someone else.</returns>
    public async Task<ReportRunResult?> RunAsync(
        string kind, DateOnly to, string trigger, Guid? requestedBy, bool withAnalysis, bool deliver,
        CancellationToken cancellationToken)
    {
        var (from, _, _, _) = StakeholderReportBuilder.Period(kind, to);
        var run = ReportRun.Start(kind, from, to, trigger, requestedBy, DateTime.UtcNow);
        _db.ReportRuns.Add(run);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException) when (trigger == ReportRun.Schedule)
        {
            // Unique (Kind, PeriodStart) for scheduled runs: another instance or an earlier
            // pass already took this report.
            _db.ReportRuns.Remove(run);
            return null;
        }

        try
        {
            var inputs = await new ExecutiveOverviewLoader(_db, _dynamicData).LoadAsync(withLabels: true, cancellationToken);
            var report = StakeholderReportBuilder.Build(inputs, kind, to);
            var (analysis, note) = await AnalyseAsync(run, report, withAnalysis, cancellationToken);

            if (!_settings.IsPdfConfigured)
                throw new InvalidOperationException("The PDF service isn't set up (Reports__PdfServiceUrl).");

            var a4 = await _pdf.RenderAsync(_renderer.RenderHtml(report, analysis, note, phone: false), phone: false, cancellationToken);
            var phonePdf = await _pdf.RenderAsync(_renderer.RenderHtml(report, analysis, note, phone: true), phone: true, cancellationToken);

            var baseName = $"reports/{to:yyyy-MM}/{kind}-{to:yyyy-MM-dd}-{run.Id:N}";
            await UploadAsync($"{baseName}-a4.pdf", a4, cancellationToken);
            await UploadAsync($"{baseName}-phone.pdf", phonePdf, cancellationToken);

            if (deliver) await DeliverAsync(run, report, phonePdf, cancellationToken);

            run.Complete($"{baseName}-a4.pdf", $"{baseName}-phone.pdf", DateTime.UtcNow);
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Stakeholder report {Kind} {To} failed.", kind, to);
            run.Fail(Redact(ex.Message)!, DateTime.UtcNow);
            await _db.SaveChangesAsync(CancellationToken.None);
        }

        return new ReportRunResult(run, false);
    }

    private async Task<(ReportAnalysis? Analysis, string? Note)> AnalyseAsync(
        ReportRun run, StakeholderReport report, bool withAnalysis, CancellationToken cancellationToken)
    {
        if (!withAnalysis)
        {
            run.RecordAnalysis(ReportRun.AnalysisOff, 0, 0, 0);
            return (null, null);
        }
        if (!_settings.IsAnalysisConfigured)
        {
            run.RecordAnalysis(ReportRun.AnalysisNotConfigured, 0, 0, 0);
            return (null, "التحليل المكتوب غير مفعّل بعد.");
        }

        var figures = ReportFigures.ToJson(report);
        string? correction = null;
        for (var attempt = 1; attempt <= 2; attempt++)
        {
            // One Claude call at a time in this process: the budget check, the reservation and the
            // call happen together, so two reports at once can't both squeeze under the cap. (The
            // API runs as one instance; the reservation also covers a second one, minus the race.)
            await ClaudeGate.WaitAsync(cancellationToken);
            AnalystResult result;
            try
            {
                var spent = await SpentThisMonthAsync(DateTime.UtcNow, cancellationToken);
                var worstCase = WorstCaseCallCost(figures);
                if (spent + worstCase > _settings.MonthlyBudgetUsd)
                {
                    _logger.LogWarning("Report analysis skipped: ${Spent} spent this month of ${Budget}.", spent, _settings.MonthlyBudgetUsd);
                    run.RecordAnalysis(ReportRun.AnalysisSkippedBudget, 0, 0, 0);
                    await _db.SaveChangesAsync(cancellationToken);
                    return (null, "لم يُكتب التحليل لهذا التقرير لأن ميزانية التحليل الشهرية اكتملت. الأرقام والرسوم كاملة.");
                }

                run.ReserveCost(worstCase);
                await _db.SaveChangesAsync(cancellationToken);
                try
                {
                    result = await _analyst.AnalyseAsync(report.Kind, figures, correction, cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Nothing came back, so nothing to bill: release the reservation.
                    result = new AnalystResult(null, 0, 0, ex.Message);
                }
            }
            finally
            {
                ClaudeGate.Release();
            }

            var cost = _settings.Cost(result.InputTokens, result.OutputTokens);
            if (result.Analysis is null)
            {
                run.RecordAnalysis(ReportRun.AnalysisFailed, result.InputTokens, result.OutputTokens, cost, Redact(result.Error));
                await _db.SaveChangesAsync(cancellationToken);
                return (null, "تعذّر إعداد التحليل المكتوب لهذا التقرير. الأرقام والرسوم كاملة.");
            }

            var problems = ReportNumberCheck.UnknownNumbers(result.Analysis, figures)
                .Select(n => $"number {n} is not in the figures")
                .Concat(ReportOutputCheck.Problems(result.Analysis, report))
                .ToList();
            if (problems.Count == 0)
            {
                run.RecordAnalysis(ReportRun.AnalysisWritten, result.InputTokens, result.OutputTokens, cost);
                await _db.SaveChangesAsync(cancellationToken);
                return (result.Analysis, null);
            }

            _logger.LogWarning("Report analysis rejected: {Problems}", string.Join("; ", problems));
            run.RecordAnalysis(ReportRun.AnalysisFailed, result.InputTokens, result.OutputTokens, cost,
                $"Analysis rejected: {string.Join("; ", problems)}");
            await _db.SaveChangesAsync(cancellationToken);
            correction = "Your previous answer was rejected by the automatic checks: " + string.Join("; ", problems) +
                         ". Rewrite it: copy numbers exactly from the figures (or use words), no links, e-mail " +
                         "addresses, markup or code, only the project codes in the figures, and keep to the lengths.";
        }

        return (null, "تعذّر التحقق من التحليل المكتوب، فأُرسل التقرير بدونه. الأرقام والرسوم كاملة.");
    }

    private static readonly SemaphoreSlim ClaudeGate = new(1, 1);

    /// <summary>Errors are stored and shown on the page: never with the relay address (it holds the relay key).</summary>
    private string? Redact(string? error)
    {
        if (error is null || string.IsNullOrWhiteSpace(_settings.AnthropicBaseUrl)) return error;
        var secret = _settings.AnthropicBaseUrl.Trim().TrimEnd('/');
        var path = Uri.TryCreate(secret, UriKind.Absolute, out var uri) ? uri.AbsolutePath.Trim('/') : null;
        var redacted = error.Replace(secret, "[relay]", StringComparison.OrdinalIgnoreCase);
        return string.IsNullOrEmpty(path) ? redacted : redacted.Replace(path, "[key]", StringComparison.Ordinal);
    }

    private async Task UploadAsync(string name, byte[] pdf, CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream(pdf);
        await _blobs.UploadAsync(name, stream, "application/pdf", cancellationToken);
    }

    private async Task DeliverAsync(ReportRun run, StakeholderReport report, byte[] pdf, CancellationToken cancellationToken)
    {
        var recipients = (await _db.ReportRecipients.ToListAsync(cancellationToken)).Where(r => r.Receives(run.Kind)).ToList();
        if (recipients.Count == 0) return;
        if (!_whatsAppSettings.IsConfigured)
            throw new InvalidOperationException("WhatsApp isn't set up (WhatsApp__PhoneNumberId / WhatsApp__AccessToken).");

        var title = ReportTexts.Title(run.Kind);
        var period = ReportTexts.Period(report);
        var fileName = ReportTexts.FileName(report);
        foreach (var recipient in recipients)
        {
            var delivery = ReportDelivery.Start(run.Id, recipient.Id, recipient.PhoneNumber, DateTime.UtcNow);
            _db.ReportDeliveries.Add(delivery);
            await _db.SaveChangesAsync(cancellationToken);

            var result = await _whatsApp.SendDocumentAsync(
                recipient.PhoneNumber, _settings.WhatsAppTemplate, pdf, fileName,
                new[] { recipient.Name, title, period }, cancellationToken);
            if (result.Success) delivery.MarkSent(result.MessageId, DateTime.UtcNow);
            else delivery.MarkFailed(result.Error ?? "Unknown error");
            await _db.SaveChangesAsync(cancellationToken);
        }
    }
}

/// <summary>Arabic names and dates shared by the PDF, the WhatsApp message and the file name.</summary>
public static class ReportTexts
{
    private static readonly string[] Months =
        { "يناير", "فبراير", "مارس", "أبريل", "مايو", "يونيو", "يوليو", "أغسطس", "سبتمبر", "أكتوبر", "نوفمبر", "ديسمبر" };

    private static readonly Dictionary<DayOfWeek, string> Days = new()
    {
        [DayOfWeek.Saturday] = "السبت", [DayOfWeek.Sunday] = "الأحد", [DayOfWeek.Monday] = "الاثنين",
        [DayOfWeek.Tuesday] = "الثلاثاء", [DayOfWeek.Wednesday] = "الأربعاء", [DayOfWeek.Thursday] = "الخميس",
        [DayOfWeek.Friday] = "الجمعة",
    };

    public static string Title(string kind) => kind switch
    {
        ReportRun.Daily => "التقرير اليومي",
        ReportRun.Weekly => "التقرير الأسبوعي",
        _ => "التقرير الشهري",
    };

    public static string Date(DateOnly d, bool weekday = false) =>
        (weekday ? Days[d.DayOfWeek] + " " : "") + $"{d.Day} {Months[d.Month - 1]} {d.Year}";

    public static string Period(StakeholderReport r) => r.Kind switch
    {
        ReportRun.Daily => Date(r.To, weekday: true),
        ReportRun.Weekly => $"الأسبوع {r.From.Day} {Months[r.From.Month - 1]} - {Date(r.To)}",
        _ => $"شهر {Months[r.To.Month - 1]} {r.To.Year}",
    };

    public static string FileName(StakeholderReport r) => $"Asas-{r.Kind}-report-{r.To:yyyy-MM-dd}.pdf";
}
