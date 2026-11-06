using Platform.Domain.Common;

namespace Platform.Domain.Reports;

/// <summary>
/// One generated stakeholder report (daily, weekly or monthly), with what Claude's analysis
/// cost. Scheduled runs are inserted BEFORE any work under a unique (Kind, PeriodStart) key
/// for <see cref="Schedule"/> runs, so a restart or a second API instance can never send the
/// same report twice: whoever inserts the row does the work. Previews and "send now" runs are
/// never blocked by that key, but their Claude cost counts towards the monthly cap like any
/// other run.
/// </summary>
public class ReportRun : BaseEntity
{
    public const string Daily = "daily";
    public const string Weekly = "weekly";
    public const string Monthly = "monthly";
    public static readonly IReadOnlyList<string> Kinds = new[] { Daily, Weekly, Monthly };

    public const string Schedule = "schedule";
    public const string Preview = "preview";
    public const string SendNow = "send-now";

    public const string Running = "running";
    public const string Done = "done";
    public const string Failed = "failed";

    // What happened to the written analysis.
    public const string AnalysisWritten = "written";
    public const string AnalysisSkippedBudget = "skipped-budget";
    public const string AnalysisNotConfigured = "not-configured";
    public const string AnalysisFailed = "failed";
    public const string AnalysisOff = "off";

    public string Kind { get; private set; } = default!;
    public DateOnly PeriodStart { get; private set; }
    public DateOnly PeriodEnd { get; private set; }
    public string Trigger { get; private set; } = default!;
    public Guid? RequestedByUserId { get; private set; }

    public string Status { get; private set; } = default!;
    public string? Analysis { get; private set; }
    public int InputTokens { get; private set; }
    public int OutputTokens { get; private set; }

    /// <summary>US dollars, from the token counts and the configured per-token prices.</summary>
    public decimal CostUsd { get; private set; }

    public string? Error { get; private set; }
    public string? PdfBlobName { get; private set; }
    public string? PhonePdfBlobName { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }

    private ReportRun() { }

    public static ReportRun Start(string kind, DateOnly periodStart, DateOnly periodEnd, string trigger, Guid? requestedBy, DateTime nowUtc) => new()
    {
        Kind = kind,
        PeriodStart = periodStart,
        PeriodEnd = periodEnd,
        Trigger = trigger,
        RequestedByUserId = requestedBy,
        Status = Running,
        CreatedAtUtc = nowUtc,
    };

    // Worst-case cost held on CostUsd while a Claude call is in flight (not a column).
    private decimal _reserved;

    /// <summary>
    /// Counts a call's worst-case cost BEFORE it is made, so a report running at the same time
    /// sees it in the monthly total. Replaced by the real cost in <see cref="RecordAnalysis"/>; if
    /// the process dies mid-call the reservation stays, which errs on the safe side.
    /// </summary>
    public void ReserveCost(decimal usd)
    {
        CostUsd += usd;
        _reserved += usd;
    }

    /// <summary>Recorded as soon as Claude answers, so the cost counts even if a later step fails.</summary>
    public void RecordAnalysis(string outcome, int inputTokens, int outputTokens, decimal costUsd, string? error = null)
    {
        Analysis = outcome;
        InputTokens += inputTokens;
        OutputTokens += outputTokens;
        CostUsd += costUsd - _reserved;
        _reserved = 0;
        if (error is not null) Error = Truncate(error);
    }

    public void Complete(string? pdfBlobName, string? phonePdfBlobName, DateTime nowUtc)
    {
        Status = Done;
        PdfBlobName = pdfBlobName;
        PhonePdfBlobName = phonePdfBlobName;
        CompletedAtUtc = nowUtc;
    }

    /// <summary>Keeps an earlier error (the analysis's) in front of this one, so neither is lost.</summary>
    public void Fail(string error, DateTime nowUtc)
    {
        Status = Failed;
        Error = Truncate(Error is null ? error : $"{Error} | {error}");
        CompletedAtUtc = nowUtc;
    }

    private static string Truncate(string text) => text.Length > 2000 ? text[..2000] : text;
}

/// <summary>One report sent (or tried) to one recipient on WhatsApp.</summary>
public class ReportDelivery : BaseEntity
{
    public Guid ReportRunId { get; private set; }
    public Guid RecipientId { get; private set; }
    public string PhoneNumber { get; private set; } = default!;
    public string Status { get; private set; } = default!;
    public string? ProviderMessageId { get; private set; }
    public string? Error { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? SentAtUtc { get; private set; }

    private ReportDelivery() { }

    public static ReportDelivery Start(Guid runId, Guid recipientId, string phoneNumber, DateTime nowUtc) => new()
    {
        ReportRunId = runId,
        RecipientId = recipientId,
        PhoneNumber = phoneNumber,
        Status = ReportRun.Running,
        CreatedAtUtc = nowUtc,
    };

    public void MarkSent(string? messageId, DateTime nowUtc)
    {
        Status = "sent";
        ProviderMessageId = messageId;
        SentAtUtc = nowUtc;
    }

    public void MarkFailed(string error)
    {
        Status = ReportRun.Failed;
        Error = error.Length > 1000 ? error[..1000] : error;
    }
}
