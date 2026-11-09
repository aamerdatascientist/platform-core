using Platform.Application.StageProgress;

namespace Platform.Application.Analytics.Queries.GetExecutiveOverview;

// ---------------------------------------------------------------------------------------
// Plain input rows. The handler maps the seven live forms' dynamic rows into these, so the
// calculator below never touches a database, a FormDefinition or a loosely-typed value -
// which is what lets every number on the dashboard be unit-tested with hand-written rows.
// ---------------------------------------------------------------------------------------

public record OverviewProjectRow(
    Guid Id, string Code, string Name, string? City, string? Manager, string? Status,
    DateOnly? StartDate, DateOnly? ExpectedCompletion);

/// <summary>
/// One unit of daily-report activity: a v2 report's stage block (Phase = the stage code), or a
/// whole v1 report (Phase = its old phase). Several blocks of one report share
/// <paramref name="SubmissionId"/>, so per-report figures (crew, delays) count it once.
/// Progress itself comes from the stage blocks (ProjectStageData), never from these rows.
/// </summary>
public record OverviewReportRow(
    Guid ProjectId, DateOnly Date, string Phase, string? UnitKey, int CrewCount, string? Weather,
    bool HadDelay, string? DelayCause, bool HasProblem, string? ProblemDescription, bool PlanMet,
    Guid SubmissionId = default);

/// <summary><paramref name="IsInflow"/> false means an outflow; <paramref name="Reason"/> is outflow-only.</summary>
public record OverviewStockRow(
    Guid ProjectId, DateOnly Date, bool IsInflow, string Material, bool IsCustomMaterial,
    decimal Quantity, string? Unit, string? Reason);


// ---------------------------------------------------------------------------------------
// Output
// ---------------------------------------------------------------------------------------

public record ExecutiveOverviewDto(
    DateOnly AsOf,
    DateOnly? DataFrom,
    Guid? SelectedProjectId,
    string? SelectedPhase,
    int ProjectsWithoutReports,
    OverviewTilesDto Tiles,
    IReadOnlyList<OverviewProjectDto> Projects,
    IReadOnlyList<OverviewWeekPlanMetDto> PlanMetByWeek,
    IReadOnlyList<OverviewWeekWorkersDto> WorkersByWeek,
    IReadOnlyList<OverviewWeatherDto> PlanMetByWeather,
    int DelayedReportCount,
    IReadOnlyList<OverviewLabelCountDto> DelayCauses,
    IReadOnlyList<OverviewStockMaterialDto> StockByMaterial,
    int StockIssueCount,
    IReadOnlyList<OverviewLabelCountDto> IssueReasons,
    int ProblemReportCount,
    IReadOnlyList<OverviewProblemDto> LatestProblems,
    /// <summary>The stages of the plans in scope, in plan order, with their names - for the
    /// stage filter and every stage label on the page (2026-11-10).</summary>
    IReadOnlyList<OverviewStageInfoDto>? Stages = null);

public record OverviewStageInfoDto(string Code, string NameAr, string NameEn);

/// <summary>
/// Portfolio counts always describe every reporting project; the remaining figures describe
/// the current scope (all projects, or the selected one). Shares are 0..1; null means "no
/// reports in that window", which the page shows as a dash rather than 0%.
///
/// With a stage selected, <paramref name="StageWorkComplete"/> is that stage's days earned over
/// days planned (null without a stage). The lateness and forecast figures, and their tones
/// ("good" / "warn" / "bad", see <see cref="OverviewKpiThresholds"/>), are explained on
/// <see cref="ExecutiveOverviewCalculator.BuildLateness"/> and <see cref="ExecutiveOverviewCalculator.Forecast"/>.
/// </summary>
public record OverviewTilesDto(
    int ProjectCount, int ActiveCount, int OnHoldCount, int CompletedCount,
    double WorkComplete, int ActiveBehindCount,
    double? PlanMetLast14Days, double? PlanMetPrevious14Days, int ReportsLast14Days,
    int? WorkersOnSite, DateOnly? WorkersOnSiteDate,
    double? DelayedShareLast30Days, int DelayedReportsLast30Days, int ReportsLast30Days,
    double? StageWorkComplete, int StageEarnedDays, int StagePlannedDays,
    int LateDays, int WorkedDays, double? LateShare, string? LateDaysTone, string? LateShareTone,
    DateOnly? ForecastFinish, DateOnly? ForecastTarget, int? ForecastDaysLate, bool ForecastFinished,
    string? ForecastTone);

public record OverviewProjectDto(
    Guid Id, string Code, string Name, string? City, string? Manager, string? Status,
    IReadOnlyList<string> CurrentPhases,
    double WorkComplete, double? TimeElapsed, string ScheduleStatus, int? ScheduleGapPoints,
    IReadOnlyList<OverviewPhaseDto> Phases,
    double PlanMet, int ReportCount, DateOnly LastReportDate, int DaysSinceLastReport);

public record OverviewPhaseDto(string Phase, double Complete, string? NameAr = null, string? NameEn = null);

public record OverviewWeekPlanMetDto(DateOnly WeekStart, double? PlanMet, int ReportCount);

public record OverviewWeekWorkersDto(DateOnly WeekStart, double AverageWorkersPerDay, int ReportingDays);

public record OverviewWeatherDto(string Weather, double PlanMet, int ReportCount);

public record OverviewLabelCountDto(string Label, int Count);

public record OverviewStockMaterialDto(
    string Material, bool IsCustomMaterial, string? Unit, double Received, double Issued, double OnHand, double IssuedShare);

public record OverviewProblemDto(DateOnly Date, string Phase, string Description, string ProjectCode, string ProjectName);

/// <summary>
/// Where the three lateness KPIs turn amber and red. One place, so the page and the
/// numbers can never disagree about what counts as "bad".
/// </summary>
public static class OverviewKpiThresholds
{
    public const string Good = "good";
    public const string Warn = "warn";
    public const string Bad = "bad";

    /// <summary>Forecast finish: on or before its target is good, up to this many calendar days late is amber.</summary>
    public const int ForecastWarnDays = 30;

    /// <summary>Days late: up to 5 working days is good, up to 15 (under a floor's cycle) amber.</summary>
    public const int LateDaysGood = 5;
    public const int LateDaysWarn = 15;

    /// <summary>Share of worked days that missed their plan: under 10% good, up to 25% amber.</summary>
    public const double LateShareGood = 0.10;
    public const double LateShareWarn = 0.25;

    public static string? ForecastTone(int? daysLate) => daysLate switch
    {
        null => null,
        <= 0 => Good,
        <= ForecastWarnDays => Warn,
        _ => Bad,
    };

    public static string LateDaysTone(int lateDays) =>
        lateDays <= LateDaysGood ? Good : lateDays <= LateDaysWarn ? Warn : Bad;

    public static string? LateShareTone(double? share) => share switch
    {
        null => null,
        < LateShareGood => Good,
        <= LateShareWarn => Warn,
        _ => Bad,
    };
}

/// <summary>
/// Every figure on the Executive Overview, computed from plain rows.
///
/// Work complete follows the method in the "Daily Progress Report - single conditional form"
/// design: a unit earns one day for each report that says the day's plan was met, capped at
/// that unit's planned days; a phase or project is days earned over days planned. A day that
/// missed its plan earns nothing, so a run of missed days shows up as slipping against the
/// calendar well before anything is formally late.
/// </summary>
public static class ExecutiveOverviewCalculator
{
    /// <summary>A project is ahead/behind when work complete and time elapsed differ by more than this.</summary>
    public const int ScheduleTolerancePoints = 5;

    /// <summary>A weather condition needs at least this many site reports to be charted.</summary>
    public const int MinimumReportsPerWeather = 5;

    public const int MaxDelayCauses = 7;
    public const int MaxLatestProblems = 6;
    public const int MaxWeeks = 26;

    private static readonly string[] WeatherOrder = { "clear", "extreme_heat", "sandstorm", "rain", "other" };

    private static readonly string[] ReasonOrder =
        { "used_in_construction", "transferred", "returned_to_supplier", "damaged_or_wasted", "other" };

    public static ExecutiveOverviewDto Calculate(
        IReadOnlyCollection<OverviewProjectRow> projects,
        IReadOnlyDictionary<Guid, ProjectStageData> stageDataByProject,
        IReadOnlyCollection<OverviewReportRow> reports,
        IReadOnlyCollection<OverviewStockRow> stock,
        DateOnly asOf,
        Guid? selectedProjectId,
        string? selectedPhase = null)
    {
        var reportsByProject = reports.GroupBy(r => r.ProjectId).ToDictionary(g => g.Key, g => g.ToList());

        // Only projects that have at least one daily report can be measured at all.
        var reporting = projects.Where(p => reportsByProject.ContainsKey(p.Id)).ToList();
        var summaries = reporting
            .Select(p => SummariseProject(p, stageDataByProject.GetValueOrDefault(p.Id), reportsByProject[p.Id], asOf))
            .OrderBy(s => s.Dto.Code, StringComparer.Ordinal)
            .ToList();

        var reportingIds = reporting.Select(p => p.Id).ToHashSet();
        var allReports = reports.Where(r => reportingIds.Contains(r.ProjectId)).ToList();
        var scopeSummaries = selectedProjectId is { } selected
            ? summaries.Where(s => s.Dto.Id == selected).ToList()
            : summaries;
        // A selected stage narrows every report-based figure; the project table stays whole.
        var scopeReports = allReports
            .Where(r => selectedProjectId is not { } id || r.ProjectId == id)
            .Where(r => selectedPhase is null || r.Phase == selectedPhase)
            .ToList();
        var scopeStock = selectedProjectId is { } selectedForStock
            ? stock.Where(s => s.ProjectId == selectedForStock).ToList()
            : stock.ToList();

        var weeks = BuildWeeks(allReports, asOf);
        // A report with several stage blocks is one report for delays and crew.
        var delayed = OncePerReport(scopeReports.Where(r => r.HadDelay)).ToList();
        var problems = scopeReports.Where(r => r.HasProblem).ToList();
        var issues = scopeStock.Where(s => !s.IsInflow).ToList();
        var projectById = reporting.ToDictionary(p => p.Id);

        return new ExecutiveOverviewDto(
            AsOf: asOf,
            DataFrom: allReports.Count == 0 ? null : allReports.Min(r => r.Date),
            SelectedProjectId: selectedProjectId,
            SelectedPhase: selectedPhase,
            ProjectsWithoutReports: projects.Count - reporting.Count,
            Tiles: BuildTiles(summaries, scopeSummaries, scopeReports, reportsByProject, selectedPhase, asOf),
            Projects: summaries.Select(s => s.Dto).ToList(),
            PlanMetByWeek: weeks.Select(w => PlanMetForWeek(w, scopeReports)).ToList(),
            WorkersByWeek: weeks.Select(w => WorkersForWeek(w, scopeReports)).ToList(),
            PlanMetByWeather: BuildWeather(scopeReports),
            DelayedReportCount: delayed.Count,
            DelayCauses: delayed
                .Where(r => !string.IsNullOrWhiteSpace(r.DelayCause))
                .GroupBy(r => r.DelayCause!.Trim())
                .Select(g => new OverviewLabelCountDto(g.Key, g.Count()))
                .OrderByDescending(c => c.Count).ThenBy(c => c.Label, StringComparer.Ordinal)
                .Take(MaxDelayCauses)
                .ToList(),
            StockByMaterial: BuildStock(scopeStock),
            StockIssueCount: issues.Count,
            IssueReasons: BuildReasons(issues),
            ProblemReportCount: problems.Count,
            LatestProblems: problems
                .OrderByDescending(r => r.Date)
                .ThenBy(r => projectById[r.ProjectId].Code, StringComparer.Ordinal)
                .Take(MaxLatestProblems)
                .Select(r => new OverviewProblemDto(
                    r.Date, r.Phase, r.ProblemDescription ?? string.Empty,
                    projectById[r.ProjectId].Code, projectById[r.ProjectId].Name))
                .ToList(),
            Stages: StageInfos(summaries, selectedProjectId));
    }

    /// <summary>One row per report: blocks of the same submission collapse to the first.</summary>
    public static IEnumerable<OverviewReportRow> OncePerReport(IEnumerable<OverviewReportRow> rows) =>
        rows.GroupBy(r => r.SubmissionId == Guid.Empty ? (object)r : r.SubmissionId).Select(g => g.First());

    private static IReadOnlyList<OverviewStageInfoDto> StageInfos(IReadOnlyList<ProjectSummary> summaries, Guid? selectedProjectId)
    {
        var scope = selectedProjectId is { } id ? summaries.Where(s => s.Dto.Id == id) : summaries;
        var seen = new Dictionary<string, (int Order, OverviewStageInfoDto Info)>();
        foreach (var summary in scope)
        {
            if (summary.Progress is null) continue;
            var order = 0;
            foreach (var stage in summary.Progress.Schedule.Stages)
                if (!seen.ContainsKey(stage.StageCode)) seen[stage.StageCode] = (order++ * 1000 + seen.Count, new OverviewStageInfoDto(stage.StageCode, stage.NameAr, stage.NameEn));
                else order++;
        }
        return seen.Values.OrderBy(v => v.Order).Select(v => v.Info).ToList();
    }

    // ----------------------------------------------------------------------------- projects

    private sealed record ProjectSummary(
        OverviewProjectDto Dto, OverviewProjectRow Project, int EarnedDays, int PlannedDays,
        IReadOnlyDictionary<string, int> EarnedByPhase, IReadOnlyDictionary<string, int> PlannedByPhase,
        ProjectProgress? Progress);

    private static ProjectSummary SummariseProject(
        OverviewProjectRow project, ProjectStageData? stageData, IReadOnlyList<OverviewReportRow> reports, DateOnly asOf)
    {
        // Progress = days earned over planned unit-days from the project's own stage plan
        // (StageProgress). A project without a plan yet shows 0% and "unknown" schedule.
        var progress = stageData is null ? null : ProjectProgressCalculator.Compute(stageData, asOf);
        var stages = progress?.Stages ?? [];
        var phases = stages.Select(s => new OverviewPhaseDto(s.StageCode, s.Complete, s.NameAr, s.NameEn)).ToList();
        var earnedByPhase = stages.ToDictionary(s => s.StageCode, s => s.EarnedDays);
        var plannedByPhase = stages.ToDictionary(s => s.StageCode, s => s.PlannedDays);

        var workComplete = progress?.WorkComplete ?? 0;
        double? timeElapsed = progress is { Dates: not null } && progress.Schedule.Project.Middle.End > 0
            ? Math.Clamp((double)progress.ElapsedWorkingDays / StageDates.WithSlack(progress.Schedule.Project.Middle.End), 0d, 1d)
            : null;
        var lastReportDate = reports.Max(r => r.Date);
        var order = stages.Select(s => s.StageCode).ToList();
        var currentPhases = reports.Where(r => r.Date == lastReportDate && order.Contains(r.Phase))
            .Select(r => r.Phase).Distinct().OrderBy(order.IndexOf).ToList();

        var (scheduleStatus, gap) = ComputeSchedule(project.Status, workComplete, progress?.PlannedShare);

        var dto = new OverviewProjectDto(
            project.Id, project.Code, project.Name, project.City, project.Manager, project.Status,
            currentPhases, workComplete, timeElapsed, scheduleStatus, gap, phases,
            PlanMet: (double)reports.Count(r => r.PlanMet) / reports.Count,
            ReportCount: OncePerReport(reports).Count(),
            LastReportDate: lastReportDate,
            DaysSinceLastReport: Math.Max(0, asOf.DayNumber - lastReportDate.DayNumber));

        return new ProjectSummary(dto, project, progress?.EarnedDays ?? 0, progress?.PlannedDays ?? 0, earnedByPhase, plannedByPhase, progress);
    }

    /// <summary>
    /// Share of the start-to-expected-completion span that has passed, 0..1. Null when either
    /// date is missing or the span is zero or negative (nothing validates the two dates
    /// against each other at entry) - "can't tell", never a made-up 0% or 100%.
    /// </summary>
    public static double? ComputeTimeElapsed(DateOnly? start, DateOnly? expectedCompletion, DateOnly asOf)
    {
        if (start is not { } s || expectedCompletion is not { } e) return null;
        var span = e.DayNumber - s.DayNumber;
        if (span <= 0) return null;
        return Math.Clamp((double)(asOf.DayNumber - s.DayNumber) / span, 0d, 1d);
    }

    /// <summary>
    /// "finished" for a completed project; otherwise ahead/behind/on_track from the gap between
    /// work complete and the share of the work the plan expects by now (the midpoint of the
    /// fastest and slowest schedules, with the slack - owner, 2026-11-10), in whole percentage
    /// points; "unknown" without a start date.
    /// The gap is taken between the two ROUNDED percentages, so it always equals the
    /// difference of the two figures the page shows next to it.
    /// </summary>
    public static (string Status, int? GapPoints) ComputeSchedule(string? projectStatus, double workComplete, double? plannedShare)
    {
        int? gap = plannedShare is { } expected ? ToPercent(workComplete) - ToPercent(expected) : null;
        if (string.Equals(projectStatus, "completed", StringComparison.OrdinalIgnoreCase)) return ("finished", gap);
        if (gap is not { } points) return ("unknown", null);
        if (points > ScheduleTolerancePoints) return ("ahead", points);
        if (points < -ScheduleTolerancePoints) return ("behind", points);
        return ("on_track", points);
    }

    public static int ToPercent(double share) => (int)Math.Round(share * 100, MidpointRounding.AwayFromZero);

    // -------------------------------------------------------------------------------- tiles

    private static OverviewTilesDto BuildTiles(
        IReadOnlyList<ProjectSummary> summaries,
        IReadOnlyList<ProjectSummary> scopeSummaries,
        IReadOnlyList<OverviewReportRow> scopeReports,
        IReadOnlyDictionary<Guid, List<OverviewReportRow>> reportsByProject,
        string? selectedPhase,
        DateOnly asOf)
    {
        int CountStatus(string status) =>
            summaries.Count(s => string.Equals(s.Dto.Status, status, StringComparison.OrdinalIgnoreCase));

        var planned = summaries.Sum(s => s.PlannedDays);
        var last14 = InWindow(scopeReports, asOf, 0, 14);
        var previous14 = InWindow(scopeReports, asOf, 14, 28);
        var last30 = InWindow(scopeReports, asOf, 0, 30);

        DateOnly? latestDay = scopeReports.Count == 0 ? null : scopeReports.Max(r => r.Date);

        var stageEarned = selectedPhase is null ? 0 : scopeSummaries.Sum(s => s.EarnedByPhase.GetValueOrDefault(selectedPhase));
        var stagePlanned = selectedPhase is null ? 0 : scopeSummaries.Sum(s => s.PlannedByPhase.GetValueOrDefault(selectedPhase));
        var (lateDays, workedDays) = BuildLateness(scopeReports);
        double? lateShare = workedDays == 0 ? null : (double)lateDays / workedDays;

        // Several projects: the latest forecast finish, judged by the worst slip among them.
        var forecasts = scopeSummaries
            .Select(s => Forecast(s.Project, s.Progress, reportsByProject[s.Dto.Id], selectedPhase, asOf))
            .OfType<ProjectForecast>()
            .ToList();
        var forecastDaysLate = forecasts.Select(f => f.DaysLate).OfType<int>().DefaultIfEmpty().Max();
        int? worstSlip = forecasts.Any(f => f.DaysLate is not null) ? forecastDaysLate : null;

        return new OverviewTilesDto(
            ProjectCount: summaries.Count,
            ActiveCount: CountStatus("active"),
            OnHoldCount: CountStatus("on_hold"),
            CompletedCount: CountStatus("completed"),
            WorkComplete: planned == 0 ? 0 : (double)summaries.Sum(s => s.EarnedDays) / planned,
            ActiveBehindCount: summaries.Count(s =>
                string.Equals(s.Dto.Status, "active", StringComparison.OrdinalIgnoreCase) && s.Dto.ScheduleStatus == "behind"),
            PlanMetLast14Days: Share(last14, r => r.PlanMet),
            PlanMetPrevious14Days: Share(previous14, r => r.PlanMet),
            ReportsLast14Days: last14.Count,
            WorkersOnSite: latestDay is { } day ? OncePerReport(scopeReports.Where(r => r.Date == day)).Sum(r => r.CrewCount) : null,
            WorkersOnSiteDate: latestDay,
            DelayedShareLast30Days: Share(OncePerReport(last30).ToList(), r => r.HadDelay),
            DelayedReportsLast30Days: OncePerReport(last30).Count(r => r.HadDelay),
            ReportsLast30Days: OncePerReport(last30).Count(),
            StageWorkComplete: selectedPhase is null ? null : stagePlanned == 0 ? 0 : (double)stageEarned / stagePlanned,
            StageEarnedDays: stageEarned,
            StagePlannedDays: stagePlanned,
            LateDays: lateDays,
            WorkedDays: workedDays,
            LateShare: lateShare,
            LateDaysTone: workedDays == 0 ? null : OverviewKpiThresholds.LateDaysTone(lateDays),
            LateShareTone: OverviewKpiThresholds.LateShareTone(lateShare),
            ForecastFinish: forecasts.Count == 0 ? null : forecasts.Max(f => f.Finish),
            ForecastTarget: forecasts.Count == 0 ? null : forecasts.Select(f => f.Target).Max(),
            ForecastDaysLate: worstSlip,
            ForecastFinished: forecasts.Count > 0 && forecasts.All(f => f.Finished),
            ForecastTone: OverviewKpiThresholds.ForecastTone(worstSlip));
    }

    // ----------------------------------------------------------------------------- lateness

    /// <summary>
    /// Days late = working days on which the work in scope missed its plan: a project's report
    /// date counts as late when none of that day's reports in scope met its plan (two fronts
    /// on one day, one on plan, is not a lost day). Worked days = that project's report dates
    /// in scope. Both add up across projects, one per project per date.
    /// </summary>
    public static (int LateDays, int WorkedDays) BuildLateness(IEnumerable<OverviewReportRow> scopeReports)
    {
        var days = scopeReports.GroupBy(r => (r.ProjectId, r.Date)).ToList();
        return (days.Count(d => !d.Any(r => r.PlanMet)), days.Count);
    }

    // ----------------------------------------------------------------------------- forecast

    public sealed record ProjectForecast(DateOnly Finish, DateOnly? Target, int? DaysLate, bool Finished);

    /// <summary>Plan-met share over this many days sets the pace a forecast assumes.</summary>
    public const int ForecastPaceDays = 30;

    /// <summary>
    /// When the selected stage (or, with none, the whole project) will finish at the current pace.
    ///
    /// The work left is the share of planned unit-days not yet earned, laid over the stage's
    /// (or project's) planned length on the midpoint schedule; it is divided by the plan-met
    /// share of the last 30 days (each missed day earns nothing) and counted forward in working
    /// days. A finished stage reports the date of its last report instead.
    ///
    /// The target is the planned finish on the midpoint schedule with the 10% slack (pauses
    /// push it forward); without a plan, the project's expected completion date.
    /// Null when the pace is zero - no forecast can be made then, rather than an infinite one.
    /// </summary>
    public static ProjectForecast? Forecast(
        OverviewProjectRow project, ProjectProgress? progress, IReadOnlyList<OverviewReportRow> reports, string? stage, DateOnly asOf)
    {
        if (reports.Count == 0 || progress is null) return null;

        int earned, planned, length;
        DateOnly? target;
        if (stage is null)
        {
            (earned, planned) = (progress.EarnedDays, progress.PlannedDays);
            length = progress.Schedule.Project.Middle.End;
            target = progress.FinishTarget ?? project.ExpectedCompletion;
        }
        else
        {
            var summary = progress.Stages.FirstOrDefault(s => s.StageCode == stage);
            var window = progress.Schedule.Stages.FirstOrDefault(s => s.StageCode == stage);
            if (summary is null || window is null) return null;
            (earned, planned) = (summary.EarnedDays, summary.PlannedDays);
            length = window.Window.Middle.End - window.Window.Middle.Start;
            target = progress.StageTarget(stage);
        }
        if (planned == 0) return null;

        var scope = stage is null ? reports : reports.Where(r => r.Phase == stage).ToList();
        DateOnly finish;
        var finished = earned >= planned;
        if (finished)
        {
            if (scope.Count == 0) return null;
            finish = scope.Max(r => r.Date);
        }
        else
        {
            var recent = InWindow(scope, asOf, 0, ForecastPaceDays);
            var pace = Share(recent, r => r.PlanMet) ?? Share(scope, r => r.PlanMet) ?? Share(InWindow(reports, asOf, 0, ForecastPaceDays), r => r.PlanMet) ?? 0;
            if (pace <= 0) return null;
            var remaining = Math.Max(1, length) * (1 - (double)earned / planned);
            finish = AddWorkingDays(asOf, (int)Math.Ceiling(remaining / pace - 1e-9));
        }

        return new ProjectForecast(finish, target, target is { } t ? finish.DayNumber - t.DayNumber : null, finished);
    }

    /// <summary>The date <paramref name="days"/> working days after <paramref name="from"/>; Friday is the day off.</summary>
    public static DateOnly AddWorkingDays(DateOnly from, int days)
    {
        var date = from;
        while (days > 0)
        {
            date = date.AddDays(1);
            if (WorkingWeek.IsWorkingDay(date)) days--;
        }
        return date;
    }

    /// <summary>Reports dated from <paramref name="fromDaysAgo"/> (inclusive) to <paramref name="toDaysAgo"/> (exclusive) days before asOf.</summary>
    private static List<OverviewReportRow> InWindow(
        IEnumerable<OverviewReportRow> reports, DateOnly asOf, int fromDaysAgo, int toDaysAgo) =>
        reports.Where(r =>
        {
            var daysAgo = asOf.DayNumber - r.Date.DayNumber;
            return daysAgo >= fromDaysAgo && daysAgo < toDaysAgo;
        }).ToList();

    private static double? Share(IReadOnlyCollection<OverviewReportRow> reports, Func<OverviewReportRow, bool> predicate) =>
        reports.Count == 0 ? null : (double)reports.Count(predicate) / reports.Count;

    // -------------------------------------------------------------------------------- weeks

    /// <summary>Weeks run Saturday to Friday, the Saudi working week.</summary>
    public static DateOnly WeekStart(DateOnly date) => date.AddDays(-(((int)date.DayOfWeek + 1) % 7));

    /// <summary>
    /// One shared week axis for both weekly charts: from the week of the earliest report by
    /// ANY project (so the axis doesn't jump when a project is selected) to the current week,
    /// at most <see cref="MaxWeeks"/> of them. The current week is left off until it is at
    /// least two days old - one day of reports would otherwise read as that week's result.
    /// </summary>
    public static IReadOnlyList<DateOnly> BuildWeeks(IReadOnlyCollection<OverviewReportRow> allReports, DateOnly asOf)
    {
        if (allReports.Count == 0) return Array.Empty<DateOnly>();

        var first = WeekStart(allReports.Min(r => r.Date));
        var weeks = new List<DateOnly>();
        for (var week = first; week <= asOf; week = week.AddDays(7)) weeks.Add(week);

        if (weeks.Count > 1 && asOf.DayNumber - weeks[^1].DayNumber < 2) weeks.RemoveAt(weeks.Count - 1);
        return weeks.Count > MaxWeeks ? weeks.Skip(weeks.Count - MaxWeeks).ToList() : weeks;
    }

    private static OverviewWeekPlanMetDto PlanMetForWeek(DateOnly week, IReadOnlyList<OverviewReportRow> scopeReports)
    {
        var inWeek = scopeReports.Where(r => WeekStart(r.Date) == week).ToList();
        return new OverviewWeekPlanMetDto(week, Share(inWeek, r => r.PlanMet), inWeek.Count);
    }

    private static OverviewWeekWorkersDto WorkersForWeek(DateOnly week, IReadOnlyList<OverviewReportRow> scopeReports)
    {
        // Workers present per day = every report's crew for that day added up (a project can
        // file more than one report a day, one per work front), then averaged over the days
        // that actually have reports.
        var perDay = OncePerReport(scopeReports.Where(r => WeekStart(r.Date) == week))
            .GroupBy(r => r.Date)
            .Select(g => g.Sum(r => r.CrewCount))
            .ToList();
        return new OverviewWeekWorkersDto(week, perDay.Count == 0 ? 0 : perDay.Average(), perDay.Count);
    }

    // ------------------------------------------------------------------------------ weather

    private static IReadOnlyList<OverviewWeatherDto> BuildWeather(IReadOnlyList<OverviewReportRow> scopeReports)
    {
        // Paperwork happens in an office - weather says nothing about whether it met its plan.
        var site = scopeReports
            .Where(r => !r.Phase.StartsWith("paperwork", StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(r.Weather))
            .GroupBy(r => r.Weather!)
            .ToDictionary(g => g.Key, g => g.ToList());

        // Known conditions in a fixed order, then anything a future form option adds.
        var order = WeatherOrder.Concat(site.Keys.Except(WeatherOrder).OrderBy(k => k, StringComparer.Ordinal));
        return order
            .Where(w => site.TryGetValue(w, out var rows) && rows.Count >= MinimumReportsPerWeather)
            .Select(w => new OverviewWeatherDto(w, (double)site[w].Count(r => r.PlanMet) / site[w].Count, site[w].Count))
            .ToList();
    }

    // -------------------------------------------------------------------------------- stock

    private static IReadOnlyList<OverviewStockMaterialDto> BuildStock(IReadOnlyList<OverviewStockRow> scopeStock) =>
        scopeStock
            .GroupBy(s => (s.Material, s.IsCustomMaterial))
            .Select(g =>
            {
                var received = g.Where(s => s.IsInflow).Sum(s => s.Quantity);
                var issued = g.Where(s => !s.IsInflow).Sum(s => s.Quantity);
                return new OverviewStockMaterialDto(
                    g.Key.Material, g.Key.IsCustomMaterial,
                    Unit: g.Select(s => s.Unit).FirstOrDefault(u => !string.IsNullOrWhiteSpace(u)),
                    Received: (double)received,
                    Issued: (double)issued,
                    OnHand: (double)(received - issued),
                    IssuedShare: received <= 0 ? 0 : (double)(issued / received));
            })
            // The fixed-list materials first, most-consumed at the top; free-text ones after.
            .OrderBy(m => m.IsCustomMaterial)
            .ThenByDescending(m => m.IssuedShare)
            .ThenBy(m => m.Material, StringComparer.Ordinal)
            .ToList();

    private static IReadOnlyList<OverviewLabelCountDto> BuildReasons(IReadOnlyList<OverviewStockRow> issues)
    {
        var counts = issues
            .Where(s => !string.IsNullOrWhiteSpace(s.Reason))
            .GroupBy(s => s.Reason!)
            .ToDictionary(g => g.Key, g => g.Count());

        var order = ReasonOrder.Concat(counts.Keys.Except(ReasonOrder).OrderBy(k => k, StringComparer.Ordinal));
        return order.Where(counts.ContainsKey).Select(r => new OverviewLabelCountDto(r, counts[r])).ToList();
    }
}
