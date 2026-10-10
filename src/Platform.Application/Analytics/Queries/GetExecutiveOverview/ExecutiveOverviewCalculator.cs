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
/// One Daily Progress Report. <paramref name="UnitKey"/> identifies the unit worked on within
/// its phase (a milestone value, a zone/footing/floor record Id, or "trade@room" for MEP) -
/// null when the report didn't say, in which case it still counts everywhere except progress.
/// </summary>
public record OverviewReportRow(
    Guid ProjectId, DateOnly Date, string Phase, string? UnitKey, int CrewCount, string? Weather,
    bool HadDelay, string? DelayCause, bool HasProblem, string? ProblemDescription, bool PlanMet);

/// <summary><paramref name="IsInflow"/> false means an outflow; <paramref name="Reason"/> is outflow-only.</summary>
public record OverviewStockRow(
    Guid ProjectId, DateOnly Date, bool IsInflow, string Material, bool IsCustomMaterial,
    decimal Quantity, string? Unit, string? Reason);

/// <summary>How many zones/footings/floors each project has registered in the master-data forms.</summary>
public record OverviewUnitCounts(int Zones, int Footings, int Floors);

// ---------------------------------------------------------------------------------------
// Output
// ---------------------------------------------------------------------------------------

public record ExecutiveOverviewDto(
    DateOnly AsOf,
    DateOnly? DataFrom,
    Guid? SelectedProjectId,
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
    IReadOnlyList<OverviewProblemDto> LatestProblems);

/// <summary>
/// Portfolio counts always describe every reporting project; the remaining figures describe
/// the current scope (all projects, or the selected one). Shares are 0..1; null means "no
/// reports in that window", which the page shows as a dash rather than 0%.
/// </summary>
public record OverviewTilesDto(
    int ProjectCount, int ActiveCount, int OnHoldCount, int CompletedCount,
    double WorkComplete, int ActiveBehindCount,
    double? PlanMetLast14Days, double? PlanMetPrevious14Days, int ReportsLast14Days,
    int? WorkersOnSite, DateOnly? WorkersOnSiteDate,
    double? DelayedShareLast30Days, int DelayedReportsLast30Days, int ReportsLast30Days);

public record OverviewProjectDto(
    Guid Id, string Code, string Name, string? City, string? Manager, string? Status,
    IReadOnlyList<string> CurrentPhases,
    double WorkComplete, double? TimeElapsed, string ScheduleStatus, int? ScheduleGapPoints,
    IReadOnlyList<OverviewPhaseDto> Phases,
    double PlanMet, int ReportCount, DateOnly LastReportDate, int DaysSinceLastReport);

public record OverviewPhaseDto(string Phase, double Complete);

public record OverviewWeekPlanMetDto(DateOnly WeekStart, double? PlanMet, int ReportCount);

public record OverviewWeekWorkersDto(DateOnly WeekStart, double AverageWorkersPerDay, int ReportingDays);

public record OverviewWeatherDto(string Weather, double PlanMet, int ReportCount);

public record OverviewLabelCountDto(string Label, int Count);

public record OverviewStockMaterialDto(
    string Material, bool IsCustomMaterial, string? Unit, double Received, double Issued, double OnHand, double IssuedShare);

public record OverviewProblemDto(DateOnly Date, string Phase, string Description, string ProjectCode, string ProjectName);

/// <summary>
/// Planned working days per unit, used to turn "plan met" days into a work-complete figure.
///
/// PLACEHOLDERS, deliberately in one place: the live forms hold no planned schedule yet, so
/// every project is measured against the same assumed durations. Real figures belong on the
/// project's own zones/footings/floors (a "planned days" field) - when that exists, this
/// class is what gets replaced. Agreed with Aamer 2026-10-10 as acceptable for now.
/// </summary>
public static class OverviewProgressPlan
{
    public const string Paperwork = "paperwork";
    public const string Excavation = "excavation";
    public const string Foundation = "foundation";
    public const string Structural = "structural";
    public const string Mep = "mep";

    /// <summary>Display and roll-up order - the order the work actually happens in.</summary>
    public static readonly IReadOnlyList<string> Phases = new[] { Paperwork, Excavation, Foundation, Structural, Mep };

    public static readonly IReadOnlyDictionary<string, int> MilestoneDays = new Dictionary<string, int>
    {
        ["design_approval"] = 4,
        ["permit_application"] = 3,
        ["utility_approvals"] = 2,
        ["final_permit"] = 1,
    };

    public const int ZoneDays = 2;
    public const int FootingDays = 2;
    public const int FloorDays = 8;
    public const int MepTradeDays = 3;
    public const int MepTradesPerFloor = 3;

    /// <summary>Planned days for one unit of a phase; 0 for a unit this plan doesn't know.</summary>
    public static int PlannedDaysForUnit(string phase, string unitKey) => phase switch
    {
        Paperwork => MilestoneDays.TryGetValue(unitKey, out var days) ? days : 0,
        Excavation => ZoneDays,
        Foundation => FootingDays,
        Structural => FloorDays,
        Mep => MepTradeDays,
        _ => 0,
    };

    /// <summary>Planned days for a whole phase of a project, from its registered unit counts.</summary>
    public static int PlannedDaysForPhase(string phase, OverviewUnitCounts units) => phase switch
    {
        Paperwork => MilestoneDays.Values.Sum(),
        Excavation => ZoneDays * units.Zones,
        Foundation => FootingDays * units.Footings,
        Structural => FloorDays * units.Floors,
        Mep => MepTradeDays * MepTradesPerFloor * units.Floors,
        _ => 0,
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
        IReadOnlyDictionary<Guid, OverviewUnitCounts> unitCountsByProject,
        IReadOnlyCollection<OverviewReportRow> reports,
        IReadOnlyCollection<OverviewStockRow> stock,
        DateOnly asOf,
        Guid? selectedProjectId)
    {
        var reportsByProject = reports.GroupBy(r => r.ProjectId).ToDictionary(g => g.Key, g => g.ToList());

        // Only projects that have at least one daily report can be measured at all.
        var reporting = projects.Where(p => reportsByProject.ContainsKey(p.Id)).ToList();
        var summaries = reporting
            .Select(p => SummariseProject(
                p, unitCountsByProject.GetValueOrDefault(p.Id) ?? new OverviewUnitCounts(0, 0, 0), reportsByProject[p.Id], asOf))
            .OrderBy(s => s.Dto.Code, StringComparer.Ordinal)
            .ToList();

        var reportingIds = reporting.Select(p => p.Id).ToHashSet();
        var allReports = reports.Where(r => reportingIds.Contains(r.ProjectId)).ToList();
        var scopeReports = selectedProjectId is { } selected
            ? allReports.Where(r => r.ProjectId == selected).ToList()
            : allReports;
        var scopeStock = selectedProjectId is { } selectedForStock
            ? stock.Where(s => s.ProjectId == selectedForStock).ToList()
            : stock.ToList();

        var weeks = BuildWeeks(allReports, asOf);
        var delayed = scopeReports.Where(r => r.HadDelay).ToList();
        var problems = scopeReports.Where(r => r.HasProblem).ToList();
        var issues = scopeStock.Where(s => !s.IsInflow).ToList();
        var projectById = reporting.ToDictionary(p => p.Id);

        return new ExecutiveOverviewDto(
            AsOf: asOf,
            DataFrom: allReports.Count == 0 ? null : allReports.Min(r => r.Date),
            SelectedProjectId: selectedProjectId,
            ProjectsWithoutReports: projects.Count - reporting.Count,
            Tiles: BuildTiles(summaries, scopeReports, asOf),
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
                .ToList());
    }

    // ----------------------------------------------------------------------------- projects

    private sealed record ProjectSummary(OverviewProjectDto Dto, int EarnedDays, int PlannedDays);

    private static ProjectSummary SummariseProject(
        OverviewProjectRow project, OverviewUnitCounts units, IReadOnlyList<OverviewReportRow> reports, DateOnly asOf)
    {
        // Days earned per phase: each unit's "plan met" days, capped at that unit's planned days.
        var earnedByPhase = reports
            .Where(r => r.PlanMet && !string.IsNullOrWhiteSpace(r.UnitKey))
            .GroupBy(r => (r.Phase, UnitKey: r.UnitKey!))
            .Select(g => (g.Key.Phase, Earned: Math.Min(g.Count(), OverviewProgressPlan.PlannedDaysForUnit(g.Key.Phase, g.Key.UnitKey))))
            .GroupBy(x => x.Phase)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Earned));

        var phases = new List<OverviewPhaseDto>();
        int earnedTotal = 0, plannedTotal = 0;
        foreach (var phase in OverviewProgressPlan.Phases)
        {
            var planned = OverviewProgressPlan.PlannedDaysForPhase(phase, units);
            // A report can name a unit that has since been removed from the master data;
            // never let that push a phase past 100%.
            var earned = Math.Min(earnedByPhase.GetValueOrDefault(phase), planned);
            earnedTotal += earned;
            plannedTotal += planned;
            phases.Add(new OverviewPhaseDto(phase, planned == 0 ? 0 : (double)earned / planned));
        }

        var workComplete = plannedTotal == 0 ? 0 : (double)earnedTotal / plannedTotal;
        var timeElapsed = ComputeTimeElapsed(project.StartDate, project.ExpectedCompletion, asOf);
        var lastReportDate = reports.Max(r => r.Date);
        var currentPhases = OverviewProgressPlan.Phases
            .Where(phase => reports.Any(r => r.Date == lastReportDate && r.Phase == phase))
            .ToList();

        var (scheduleStatus, gap) = ComputeSchedule(project.Status, workComplete, timeElapsed);

        var dto = new OverviewProjectDto(
            project.Id, project.Code, project.Name, project.City, project.Manager, project.Status,
            currentPhases, workComplete, timeElapsed, scheduleStatus, gap, phases,
            PlanMet: (double)reports.Count(r => r.PlanMet) / reports.Count,
            ReportCount: reports.Count,
            LastReportDate: lastReportDate,
            DaysSinceLastReport: Math.Max(0, asOf.DayNumber - lastReportDate.DayNumber));

        return new ProjectSummary(dto, earnedTotal, plannedTotal);
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
    /// work complete and time elapsed, in whole percentage points; "unknown" without dates.
    /// The gap is taken between the two ROUNDED percentages, so it always equals the
    /// difference of the two figures the page shows next to it.
    /// </summary>
    public static (string Status, int? GapPoints) ComputeSchedule(string? projectStatus, double workComplete, double? timeElapsed)
    {
        int? gap = timeElapsed is { } elapsed ? ToPercent(workComplete) - ToPercent(elapsed) : null;
        if (string.Equals(projectStatus, "completed", StringComparison.OrdinalIgnoreCase)) return ("finished", gap);
        if (gap is not { } points) return ("unknown", null);
        if (points > ScheduleTolerancePoints) return ("ahead", points);
        if (points < -ScheduleTolerancePoints) return ("behind", points);
        return ("on_track", points);
    }

    public static int ToPercent(double share) => (int)Math.Round(share * 100, MidpointRounding.AwayFromZero);

    // -------------------------------------------------------------------------------- tiles

    private static OverviewTilesDto BuildTiles(
        IReadOnlyList<ProjectSummary> summaries, IReadOnlyList<OverviewReportRow> scopeReports, DateOnly asOf)
    {
        int CountStatus(string status) =>
            summaries.Count(s => string.Equals(s.Dto.Status, status, StringComparison.OrdinalIgnoreCase));

        var planned = summaries.Sum(s => s.PlannedDays);
        var last14 = InWindow(scopeReports, asOf, 0, 14);
        var previous14 = InWindow(scopeReports, asOf, 14, 28);
        var last30 = InWindow(scopeReports, asOf, 0, 30);

        DateOnly? latestDay = scopeReports.Count == 0 ? null : scopeReports.Max(r => r.Date);

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
            WorkersOnSite: latestDay is { } day ? scopeReports.Where(r => r.Date == day).Sum(r => r.CrewCount) : null,
            WorkersOnSiteDate: latestDay,
            DelayedShareLast30Days: Share(last30, r => r.HadDelay),
            DelayedReportsLast30Days: last30.Count(r => r.HadDelay),
            ReportsLast30Days: last30.Count);
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
        var perDay = scopeReports
            .Where(r => WeekStart(r.Date) == week)
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
            .Where(r => r.Phase != OverviewProgressPlan.Paperwork && !string.IsNullOrWhiteSpace(r.Weather))
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
