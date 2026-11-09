using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Platform.Application.Analytics.Queries.GetExecutiveOverview;

namespace Platform.Application.Reports;

/// <summary>The written analysis Claude returns for one report (Arabic text).</summary>
public record ReportAnalysis(
    string Summary,
    IReadOnlyList<ReportFinding> Findings,
    ReportChartNotes ChartNotes,
    IReadOnlyList<ReportProjectAnalysis> Projects,
    IReadOnlyList<ReportRecommendation> Recommendations,
    IReadOnlyList<string> Watch);

/// <summary><see cref="Tone"/>: good | warn | bad | neutral.</summary>
public record ReportFinding(string Headline, string Tone, string Title, string Text);

public record ReportChartNotes(string PlanMet, string Crew, string Causes);

public record ReportProjectAnalysis(string Code, string Situation, string Why, string Action);

/// <summary><see cref="Priority"/>: high | medium | low.</summary>
public record ReportRecommendation(string Priority, string ProjectCode, string Action, string Owner, string Due);

public record AnalystResult(ReportAnalysis? Analysis, int InputTokens, int OutputTokens, string? Error);

/// <summary>Writes the analysis. Implemented with Claude (Infrastructure).</summary>
public interface IReportAnalyst
{
    /// <param name="figuresJson">What <see cref="ReportFigures.ToJson"/> produced - the only data the analysis may use.</param>
    /// <param name="correction">On a retry: what was wrong with the previous answer.</param>
    Task<AnalystResult> AnalyseAsync(string kind, string figuresJson, string? correction, CancellationToken cancellationToken);
}

/// <summary>Settings for the stakeholder reports (section "Reports").</summary>
public class ReportSettings
{
    /// <summary>Anthropic API key. Empty = reports go out without the written analysis.</summary>
    public string? AnthropicApiKey { get; set; }

    /// <summary>
    /// Where Claude is called. Empty = Anthropic directly. The API runs in Azure Hong Kong, where
    /// Anthropic's API is not offered, so staging/production point this at the relay on Railway
    /// (Singapore): https://{relay-domain}/{relay-key} - see docs/STAGING.md.
    /// </summary>
    public string? AnthropicBaseUrl { get; set; }

    public string Model { get; set; } = "claude-opus-5-5";

    /// <summary>Hard ceiling on Claude spend per Saudi calendar month, in US dollars (owner, 2026-11-06: $15).</summary>
    public decimal MonthlyBudgetUsd { get; set; } = 15m;

    /// <summary>Claude Opus 5.5 list prices, US dollars per million tokens.</summary>
    public decimal InputUsdPerMillionTokens { get; set; } = 4m;
    public decimal OutputUsdPerMillionTokens { get; set; } = 20m;

    /// <summary>Most a single answer may write, reasoning included - also the worst case the budget check assumes.</summary>
    public int MaxOutputTokens { get; set; } = 16000;

    /// <summary>Saudi time the scheduled reports go out ("HH:mm").</summary>
    public string SendTime { get; set; } = "18:00";

    /// <summary>The PDF service (Gotenberg). Empty = no PDFs, so nothing is sent.</summary>
    public string? PdfServiceUrl { get; set; }
    public string? PdfServiceUsername { get; set; }
    public string? PdfServicePassword { get; set; }

    /// <summary>The approved WhatsApp template with a document header: body {{1}} name, {{2}} report, {{3}} period.</summary>
    public string WhatsAppTemplate { get; set; } = "stakeholder_report";

    public bool IsAnalysisConfigured => !string.IsNullOrWhiteSpace(AnthropicApiKey);
    public bool IsPdfConfigured => !string.IsNullOrWhiteSpace(PdfServiceUrl);

    public decimal Cost(int inputTokens, int outputTokens) =>
        (inputTokens * InputUsdPerMillionTokens + outputTokens * OutputUsdPerMillionTokens) / 1_000_000m;
}

/// <summary>The JSON Claude reads: the report's figures plus the plan rules it may quote.</summary>
public static class ReportFigures
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string ToJson(StakeholderReport report) => JsonSerializer.Serialize(new
    {
        report,
        planRules = new
        {
            // Each project has its own stage plan (days per stage, per floor or per side); each
            // stage in the figures carries its plannedDays and earnedDays from that plan.
            progressMethod = "one working day earned per unit per day whose plan was met, capped at the unit's planned days",
            plannedDatesSlackPercent = (int)Math.Round(StageProgress.StageDates.Slack * 100),
            forecastPaceWindowDays = ExecutiveOverviewCalculator.ForecastPaceDays,
            forecastAmberUpToDaysLate = OverviewKpiThresholds.ForecastWarnDays,
            weeklyDayOff = StageProgress.WorkingWeek.DayOff.ToString(),
        },
    }, Options);
}

/// <summary>
/// Every number in the analysis must appear in the figures it was given. Claude is told to copy
/// numbers, never to calculate new ones; this check is what holds it to that, so a stakeholder
/// never reads a figure the data doesn't support.
/// </summary>
public static class ReportNumberCheck
{
    private static readonly Regex Number = new(@"\d+(?:[.,]\d+)?", RegexOptions.Compiled);

    /// <summary>Numbers this small read as words ("two projects") and are always allowed.</summary>
    private const int AlwaysAllowedUpTo = 10;

    public static IReadOnlyList<string> UnknownNumbers(ReportAnalysis analysis, string figuresJson)
    {
        var allowed = Numbers(figuresJson).ToHashSet();
        return Texts(analysis)
            .SelectMany(Numbers)
            .Where(n => !allowed.Contains(n) && !(int.TryParse(n, out var small) && small <= AlwaysAllowedUpTo))
            .Distinct()
            .ToList();
    }

    internal static IEnumerable<string> Texts(ReportAnalysis a)
    {
        yield return a.Summary;
        foreach (var f in a.Findings) { yield return f.Headline; yield return f.Title; yield return f.Text; }
        yield return a.ChartNotes.PlanMet;
        yield return a.ChartNotes.Crew;
        yield return a.ChartNotes.Causes;
        foreach (var p in a.Projects) { yield return p.Situation; yield return p.Why; yield return p.Action; }
        foreach (var r in a.Recommendations) { yield return r.Action; yield return r.Owner; yield return r.Due; }
        foreach (var w in a.Watch) yield return w;
    }

    /// <summary>Digits normalised: Arabic-Indic to Western, leading zeros dropped ("01" -> "1").</summary>
    private static IEnumerable<string> Numbers(string text)
    {
        var western = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            western.Append(ch switch
            {
                >= '٠' and <= '٩' => (char)('0' + (ch - '٠')),
                >= '۰' and <= '۹' => (char)('0' + (ch - '۰')),
                '٫' => '.',
                _ => ch,
            });
        }
        foreach (Match m in Number.Matches(western.ToString()))
        {
            var value = m.Value.Replace(',', '.');
            if (decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var d))
                yield return d.ToString(CultureInfo.InvariantCulture);
        }
    }
}

/// <summary>
/// The rest of what an analysis must satisfy before it goes in a stakeholder's PDF - the guard
/// against prompt injection through text typed into the forms. Claude has no tools and its
/// answer is fixed-shape JSON, so the worst an injected instruction could do is change the
/// wording; this rejects the tell-tale results (links, e-mail addresses, markup or code,
/// projects that don't exist, runaway length) and the run retries once, then goes out without
/// an analysis.
/// </summary>
public static class ReportOutputCheck
{
    public const int MaxSummaryLength = 1500;
    public const int MaxTextLength = 900;
    public const int MaxFindings = 6;
    public const int MaxRecommendations = 8;
    public const int MaxWatch = 6;

    private static readonly Regex Forbidden = new(
        @"https?://|www\.|[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}|<\s*[A-Za-z/!]|```|\{\{|\}\}",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly HashSet<string> Tones = new() { "good", "warn", "bad", "neutral" };
    private static readonly HashSet<string> Priorities = new() { "high", "medium", "low" };

    public static IReadOnlyList<string> Problems(ReportAnalysis a, StakeholderReport report)
    {
        var problems = new List<string>();
        if (string.IsNullOrWhiteSpace(a.Summary)) problems.Add("the summary is empty");
        if (a.Summary.Length > MaxSummaryLength) problems.Add("the summary is too long");
        if (a.Findings.Count > MaxFindings) problems.Add($"more than {MaxFindings} findings");
        if (a.Recommendations.Count > MaxRecommendations) problems.Add($"more than {MaxRecommendations} recommendations");
        if (a.Watch.Count > MaxWatch) problems.Add($"more than {MaxWatch} watch items");
        if (ReportNumberCheck.Texts(a).Any(t => t.Length > MaxTextLength && t != a.Summary)) problems.Add("a text is too long");
        if (ReportNumberCheck.Texts(a).Any(t => Forbidden.IsMatch(t)))
            problems.Add("links, e-mail addresses, markup, code or template braces are not allowed");
        if (a.Findings.Any(f => !Tones.Contains(f.Tone))) problems.Add("unknown tone");
        if (a.Recommendations.Any(r => !Priorities.Contains(r.Priority))) problems.Add("unknown priority");

        var codes = report.Projects.Select(p => p.Code).ToHashSet();
        var unknown = a.Projects.Select(p => p.Code)
            .Concat(a.Recommendations.Select(r => r.ProjectCode).Where(c => !string.IsNullOrWhiteSpace(c)))
            .Where(c => !codes.Contains(c))
            .Distinct()
            .ToList();
        if (unknown.Count > 0) problems.Add($"project codes not in the figures: {string.Join(", ", unknown)}");
        return problems;
    }
}
