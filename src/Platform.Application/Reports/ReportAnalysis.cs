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
            workingDaysPerZone = OverviewProgressPlan.ZoneDays,
            workingDaysPerFooting = OverviewProgressPlan.FootingDays,
            workingDaysPerFloor = OverviewProgressPlan.FloorDays,
            workingDaysPerMepTradePerFloor = OverviewProgressPlan.MepTradeDays,
            paperworkMilestoneDays = OverviewProgressPlan.MilestoneDays,
            paperworkTotalDays = OverviewProgressPlan.MilestoneDays.Values.Sum(),
            forecastPaceWindowDays = ExecutiveOverviewCalculator.ForecastPaceDays,
            forecastAmberUpToDaysLate = OverviewKpiThresholds.ForecastWarnDays,
            fridayIsTheOnlyDayOff = true,
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

    private static IEnumerable<string> Texts(ReportAnalysis a)
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
