using System.Globalization;
using Platform.Application.Analytics.Queries.GetExecutiveOverview;
using Platform.Domain.Reports;

namespace Platform.Application.Reports;

/// <summary>
/// Turns the dashboard's inputs into one report's figures. Pure: no database, no clock - the
/// caller passes the rows and the last day of the period, which is what makes every number in
/// a report unit-testable. Project status (work complete, forecast) comes from
/// <see cref="ExecutiveOverviewCalculator"/> itself, so a report never disagrees with the
/// dashboard; "at the start of the period" is the same calculation with the later reports left
/// out.
/// </summary>
public static class StakeholderReportBuilder
{
    public const int DailySeriesDays = 14;
    public const int DelayCauseWindowDays = 30;
    public const int TrendWeeks = 8;
    public const int TrendMonths = 6;
    private const int MaxCauses = 6;
    private const int MaxIssues = 14;

    /// <summary>
    /// Text typed into the forms is untrusted: it is shortened and stripped of control characters
    /// before it reaches Claude or the PDF, so a long or crafted entry can't crowd out the figures.
    /// </summary>
    public const int MaxTextLength = 160;
    public const int MaxNameLength = 80;

    /// <summary>The period a report covers, ending on <paramref name="to"/>, and the one it is compared with.</summary>
    public static (DateOnly From, DateOnly To, DateOnly PreviousFrom, DateOnly PreviousTo) Period(string kind, DateOnly to)
    {
        switch (kind)
        {
            case ReportRun.Daily:
                // A day is compared with the two weeks before it - one day is too noisy to compare with one day.
                return (to, to, to.AddDays(-14), to.AddDays(-1));
            case ReportRun.Weekly:
                var weekStart = ExecutiveOverviewCalculator.WeekStart(to);
                return (weekStart, to, weekStart.AddDays(-7), to.AddDays(-7));
            case ReportRun.Monthly:
                var monthStart = new DateOnly(to.Year, to.Month, 1);
                return (monthStart, to, monthStart.AddMonths(-1), monthStart.AddDays(-1));
            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown report kind.");
        }
    }

    public static StakeholderReport Build(OverviewInputs inputs, string kind, DateOnly to)
    {
        var (from, _, previousFrom, previousTo) = Period(kind, to);
        var labels = inputs.Labels;

        var upToEnd = inputs.Reports.Where(r => r.Date <= to).ToList();
        var inPeriod = upToEnd.Where(r => r.Date >= from).ToList();
        var inPrevious = upToEnd.Where(r => r.Date >= previousFrom && r.Date <= previousTo).ToList();

        var overviewNow = ExecutiveOverviewCalculator.Calculate(
            inputs.Projects, inputs.StageData, upToEnd, inputs.Stock, to, null);

        var projects = new List<ReportProject>();
        foreach (var dto in overviewNow.Projects)
        {
            var project = inputs.Projects.First(p => p.Id == dto.Id);
            projects.Add(BuildProject(inputs, project, dto, upToEnd, kind, from, to));
        }

        // ---------------------------------------------------------------- portfolio
        var workedDays = inPeriod.Select(r => r.Date).Distinct().Count();
        var previousDays = inPrevious.Select(r => r.Date).Distinct().Count();
        var crew = inPeriod.Sum(r => r.CrewCount);
        var crewAverage = workedDays == 0 ? 0 : RoundInt((double)crew / workedDays);
        int? crewPrevious = previousDays == 0 ? null : RoundInt((double)inPrevious.Sum(r => r.CrewCount) / previousDays);
        var planMet = Percent(inPeriod);
        var planMetPrevious = Percent(inPrevious);
        var missing = projects.Sum(p => p.MissingReports);

        var portfolio = new ReportPortfolio(
            ProjectCount: projects.Count,
            Reports: inPeriod.Count,
            ExpectedReports: inPeriod.Count + (kind == ReportRun.Daily ? missing : 0),
            MissingReports: missing,
            WorkedDays: workedDays,
            Crew: crew,
            CrewAveragePerDay: crewAverage,
            CrewAveragePerDayPrevious: crewPrevious,
            CrewChange: crewPrevious is { } cp && workedDays > 0 ? crewAverage - cp : null,
            PlanMetPercent: planMet,
            PlanMetPercentPrevious: planMetPrevious,
            PlanMetChangePoints: planMet is { } a && planMetPrevious is { } b ? a - b : null,
            Delays: inPeriod.Count(r => r.HadDelay),
            Problems: inPeriod.Count(r => r.HasProblem),
            LateDays: ExecutiveOverviewCalculator.BuildLateness(inPeriod).LateDays,
            DelayCauseWindowDays: kind == ReportRun.Daily ? DelayCauseWindowDays : null);

        // ---------------------------------------------------------------- charts and lists
        var causeWindow = kind == ReportRun.Daily
            ? upToEnd.Where(r => r.Date > to.AddDays(-DelayCauseWindowDays)).ToList()
            : inPeriod;

        var projectCodes = inputs.Projects.ToDictionary(p => p.Id, p => p.Code);
        var issues = new List<ReportIssue>();
        foreach (var r in inPeriod.OrderByDescending(r => r.Date).ThenBy(r => projectCodes.GetValueOrDefault(r.ProjectId), StringComparer.Ordinal))
        {
            var unit = Clip(labels.Unit(r.Phase, r.UnitKey), MaxNameLength) ?? string.Empty;
            if (r.HasProblem && !string.IsNullOrWhiteSpace(r.ProblemDescription))
                issues.Add(new ReportIssue("problem", r.Date, projectCodes.GetValueOrDefault(r.ProjectId) ?? "", unit, Clip(r.ProblemDescription, MaxTextLength)!));
            if (r.HadDelay && !string.IsNullOrWhiteSpace(r.DelayCause) && kind == ReportRun.Daily)
                issues.Add(new ReportIssue("delay", r.Date, projectCodes.GetValueOrDefault(r.ProjectId) ?? "", unit, Clip(r.DelayCause, MaxTextLength)!));
        }
        foreach (var p in projects)
            foreach (var unit in p.MissingUnits)
                issues.Add(new ReportIssue("missing", to, p.Code, Clip(unit, MaxNameLength)!, "لم يصل تقرير اليوم"));

        return new StakeholderReport(
            Kind: kind, From: from, To: to, PreviousFrom: previousFrom, PreviousTo: previousTo,
            Portfolio: portfolio,
            Projects: projects,
            Series: Series(kind, upToEnd, from, to),
            Trend: Trend(kind, upToEnd, to),
            DelayCauses: Causes(causeWindow, MaxCauses),
            Weather: inPeriod
                .GroupBy(r => labels.Option("weather", r.Weather))
                .Where(g => g.Key.Length > 0)
                .Select(g => new ReportCount(g.Key, g.Count()))
                .OrderByDescending(c => c.Count).ThenBy(c => c.Label, StringComparer.Ordinal)
                .ToList(),
            Issues: issues.Take(MaxIssues).ToList());
    }

    private static ReportProject BuildProject(
        OverviewInputs inputs, OverviewProjectRow project, OverviewProjectDto dto,
        IReadOnlyList<OverviewReportRow> upToEnd, string kind, DateOnly from, DateOnly to)
    {
        var labels = inputs.Labels;
        var own = upToEnd.Where(r => r.ProjectId == project.Id).ToList();
        var inPeriod = own.Where(r => r.Date >= from).ToList();
        var progress = inputs.StageData.TryGetValue(project.Id, out var stageData)
            ? StageProgress.ProjectProgressCalculator.Compute(stageData, to)
            : null;

        var tilesNow = ExecutiveOverviewCalculator.Calculate(
            inputs.Projects, inputs.StageData, upToEnd, inputs.Stock, to, project.Id).Tiles;

        // The same project as it stood the day before the period began.
        var before = upToEnd.Where(r => r.Date < from).ToList();
        OverviewProjectDto? dtoStart = null;
        OverviewTilesDto? tilesStart = null;
        if (before.Any(r => r.ProjectId == project.Id))
        {
            var overviewStart = ExecutiveOverviewCalculator.Calculate(
                inputs.Projects, inputs.StageData, before, inputs.Stock, from.AddDays(-1), project.Id);
            dtoStart = overviewStart.Projects.FirstOrDefault(p => p.Id == project.Id);
            tilesStart = overviewStart.Tiles;
        }

        var stages = new List<ReportStage>();
        foreach (var phase in dto.Phases)
        {
            var summary = progress?.Stages.FirstOrDefault(s => s.StageCode == phase.Phase);
            var planned = summary?.PlannedDays ?? 0;
            var phaseReports = own.Where(r => r.FromStageBlock && r.Phase == phase.Phase).ToList();
            if (planned == 0 && phaseReports.Count == 0) continue;
            var reportDays = phaseReports.GroupBy(r => r.Date).ToList();
            var startComplete = dtoStart?.Phases.FirstOrDefault(p => p.Phase == phase.Phase)?.Complete ?? 0;
            stages.Add(new ReportStage(
                Stage: phase.Phase,
                Label: phase.NameAr ?? StageLabel(labels, phase.Phase),
                PlannedDays: planned,
                EarnedDays: summary?.EarnedDays ?? RoundInt(phase.Complete * planned),
                ReportDays: reportDays.Count,
                LostDays: reportDays.Count(d => !d.Any(r => r.PlanMet)),
                CompletePercent: ExecutiveOverviewCalculator.ToPercent(phase.Complete),
                CompletePercentAtStart: ExecutiveOverviewCalculator.ToPercent(startComplete),
                Finished: phase.Complete >= 1));
        }

        // Missing reports. A day: units the project reported on its previous working day but not
        // today. A week or month: working days in the period with no report at all, counted
        // from the project's first report on.
        var missingUnits = new List<string>();
        var missing = 0;
        if (kind == ReportRun.Daily)
        {
            var previousDay = PreviousWorkingDay(to);
            var today = inPeriod.Select(r => (r.Phase, r.UnitKey)).ToHashSet();
            missingUnits = own.Where(r => r.Date == previousDay)
                .Select(r => (r.Phase, r.UnitKey))
                .Distinct()
                .Where(u => !today.Contains(u))
                .Select(u => StageLabel(labels, u.Phase))
                .ToList();
            missing = missingUnits.Count;
        }
        else if (own.Count > 0)
        {
            var first = own.Min(r => r.Date);
            var reported = inPeriod.Select(r => r.Date).ToHashSet();
            for (var day = from > first ? from : first; day <= to; day = day.AddDays(1))
                if (IsWorkingDay(day) && !reported.Contains(day)) missing++;
        }

        var workedDays = inPeriod.Select(r => r.Date).Distinct().Count();
        var recent = own.Where(r => r.Date > to.AddDays(-ExecutiveOverviewCalculator.ForecastPaceDays)).ToList();
        var completeNow = ExecutiveOverviewCalculator.ToPercent(dto.WorkComplete);
        var completeStart = dtoStart is null ? 0 : ExecutiveOverviewCalculator.ToPercent(dtoStart.WorkComplete);

        return new ReportProject(
            Code: project.Code,
            Name: Clip(project.Name, MaxNameLength)!,
            City: Clip(project.City, MaxNameLength),
            Manager: Clip(project.Manager, MaxNameLength),
            Tone: tilesNow.ForecastTone ?? (dto.ScheduleStatus == "behind" ? OverviewKpiThresholds.Warn : OverviewKpiThresholds.Good),
            WorkCompletePercent: completeNow,
            WorkCompletePercentAtStart: completeStart,
            WorkCompleteChangePoints: completeNow - completeStart,
            TimeElapsedPercent: dto.TimeElapsed is { } elapsed ? ExecutiveOverviewCalculator.ToPercent(elapsed) : null,
            CurrentStages: dto.CurrentPhases.Select(p => StageLabel(labels, p)).ToList(),
            Stages: stages,
            ForecastFinish: tilesNow.ForecastFinish,
            // The planned finish the lateness is measured against (stage plan, midpoint + 10%).
            ContractEnd: tilesNow.ForecastTarget ?? project.ExpectedCompletion,
            DaysLate: tilesNow.ForecastDaysLate,
            DaysLateAtStart: tilesStart?.ForecastDaysLate,
            DaysLateChange: tilesNow.ForecastDaysLate is { } now && tilesStart?.ForecastDaysLate is { } start ? now - start : null,
            PlanMetPercentLast30Days: Percent(recent),
            Reports: inPeriod.Count,
            PlanMetPercent: Percent(inPeriod),
            CrewAveragePerDay: workedDays == 0 ? 0 : RoundInt((double)inPeriod.Sum(r => r.CrewCount) / workedDays),
            LateDays: ExecutiveOverviewCalculator.BuildLateness(inPeriod).LateDays,
            WorkedDays: workedDays,
            MissingReports: missing,
            UnitReports: kind == ReportRun.Daily
                ? inPeriod
                    .OrderBy(r => progress?.Schedule.Stages.ToList().FindIndex(s => s.StageCode == r.Phase) ?? 0)
                    .Select(r => new ReportUnitDay(
                        r.Phase, StageLabel(labels, r.Phase), labels.Unit(r.Phase, r.UnitKey), r.CrewCount,
                        labels.Option("weather", r.Weather), r.PlanMet,
                        r.HadDelay ? Clip(r.DelayCause, MaxTextLength) : null,
                        r.HasProblem ? Clip(r.ProblemDescription, MaxTextLength) : null))
                    .ToList()
                : Array.Empty<ReportUnitDay>(),
            MissingUnits: missingUnits,
            TopDelayCauses: Causes(kind == ReportRun.Daily ? recent : inPeriod, 3));
    }

    // -------------------------------------------------------------------- charts

    private static IReadOnlyList<ReportSeriesPoint> Series(string kind, IReadOnlyList<OverviewReportRow> reports, DateOnly from, DateOnly to)
    {
        var points = new List<ReportSeriesPoint>();
        switch (kind)
        {
            case ReportRun.Daily:
                var day = to;
                while (points.Count < DailySeriesDays)
                {
                    if (IsWorkingDay(day)) points.Add(Point(day, ShortDate(day), reports.Where(r => r.Date == day)));
                    day = day.AddDays(-1);
                }
                points.Reverse();
                break;
            case ReportRun.Weekly:
                for (var d = from; d <= to; d = d.AddDays(1))
                    if (IsWorkingDay(d)) points.Add(Point(d, ShortDate(d), reports.Where(r => r.Date == d)));
                break;
            default:
                for (var week = ExecutiveOverviewCalculator.WeekStart(from); week <= to; week = week.AddDays(7))
                {
                    var start = week < from ? from : week;
                    var end = week.AddDays(6) > to ? to : week.AddDays(6);
                    points.Add(Point(start, $"{ShortDate(start)}-{ShortDate(end)}", reports.Where(r => r.Date >= start && r.Date <= end), averageCrew: true));
                }
                break;
        }
        return points;
    }

    private static IReadOnlyList<ReportSeriesPoint> Trend(string kind, IReadOnlyList<OverviewReportRow> reports, DateOnly to)
    {
        var points = new List<ReportSeriesPoint>();
        if (kind == ReportRun.Weekly)
        {
            var lastWeek = ExecutiveOverviewCalculator.WeekStart(to);
            for (var i = TrendWeeks - 1; i >= 0; i--)
            {
                var week = lastWeek.AddDays(-7 * i);
                points.Add(Point(week, ShortDate(week), reports.Where(r => ExecutiveOverviewCalculator.WeekStart(r.Date) == week), averageCrew: true));
            }
        }
        else if (kind == ReportRun.Monthly)
        {
            var lastMonth = new DateOnly(to.Year, to.Month, 1);
            for (var i = TrendMonths - 1; i >= 0; i--)
            {
                var month = lastMonth.AddMonths(-i);
                points.Add(Point(month, $"{month.Month}/{month.Year % 100:00}", reports.Where(r => r.Date.Year == month.Year && r.Date.Month == month.Month), averageCrew: true));
            }
        }
        return points;
    }

    /// <summary>Crew: workers on that day, or for a week/month the average per day worked.</summary>
    private static ReportSeriesPoint Point(DateOnly date, string label, IEnumerable<OverviewReportRow> rows, bool averageCrew = false)
    {
        var list = rows.ToList();
        var days = list.Select(r => r.Date).Distinct().Count();
        var crew = list.Sum(r => r.CrewCount);
        return new ReportSeriesPoint(date, label, Percent(list), averageCrew && days > 0 ? RoundInt((double)crew / days) : crew, list.Count);
    }

    private static IReadOnlyList<ReportCount> Causes(IEnumerable<OverviewReportRow> rows, int max) =>
        rows.Where(r => r.HadDelay && !string.IsNullOrWhiteSpace(r.DelayCause))
            .GroupBy(r => Clip(r.DelayCause, MaxTextLength)!)
            .Select(g => new ReportCount(g.Key, g.Count()))
            .OrderByDescending(c => c.Count).ThenBy(c => c.Label, StringComparer.Ordinal)
            .Take(max)
            .ToList();

    // -------------------------------------------------------------------- helpers

    /// <summary>Trimmed, control characters and runs of spaces removed, cut to <paramref name="max"/> characters.</summary>
    public static string? Clip(string? text, int max)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;
        var clean = System.Text.RegularExpressions.Regex.Replace(
            new string(text.Select(c => char.IsControl(c) ? ' ' : c).ToArray()), @"\s{2,}", " ").Trim();
        return clean.Length <= max ? clean : clean[..(max - 1)].TrimEnd() + "…";
    }

    public static bool IsWorkingDay(DateOnly day) => Platform.Application.StageProgress.WorkingWeek.IsWorkingDay(day);

    public static DateOnly PreviousWorkingDay(DateOnly day)
    {
        var d = day.AddDays(-1);
        while (!IsWorkingDay(d)) d = d.AddDays(-1);
        return d;
    }

    private static int? Percent(IReadOnlyCollection<OverviewReportRow> rows) =>
        rows.Count == 0 ? null : ExecutiveOverviewCalculator.ToPercent((double)rows.Count(r => r.PlanMet) / rows.Count);

    private static int RoundInt(double value) => (int)Math.Round(value, MidpointRounding.AwayFromZero);

    private static string ShortDate(DateOnly d) => d.ToString("d/M", CultureInfo.InvariantCulture);

    private static string StageLabel(OverviewLabels labels, string phase)
    {
        // A stage's name from the plans; an old (v1) phase by its option label.
        var label = labels.Stage(phase);
        return label != phase ? label : phase switch
        {
            "paperwork" => "الأعمال الورقية",
            "excavation" => "الحفر",
            "foundation" => "الأساسات",
            "structural" => "الهيكل الإنشائي",
            "mep" => "الأعمال الكهروميكانيكية",
            _ => phase,
        };
    }
}
