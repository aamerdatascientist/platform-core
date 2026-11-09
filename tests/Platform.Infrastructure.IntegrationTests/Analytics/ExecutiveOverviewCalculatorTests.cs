using FluentAssertions;
using Platform.Application.Analytics.Queries.GetExecutiveOverview;
using Xunit;

namespace Platform.Infrastructure.IntegrationTests.Analytics;

/// <summary>
/// The Executive Overview's arithmetic, tested on hand-written rows with no database: every
/// figure on the page comes out of ExecutiveOverviewCalculator, so this is where a wrong
/// number would be caught.
/// </summary>
public class ExecutiveOverviewCalculatorTests
{
    // Saturday 10 Oct 2026 - the first day of a Saudi working week.
    private static readonly DateOnly AsOf = new(2026, 10, 10);

    private static readonly Guid ProjectA = Guid.NewGuid();
    private static readonly Guid ProjectB = Guid.NewGuid();
    private static readonly string Zone1 = Guid.NewGuid().ToString();
    private static readonly string Zone2 = Guid.NewGuid().ToString();

    private static OverviewProjectRow Project(
        Guid id, string code, string status = "active", DateOnly? start = null, DateOnly? expected = null) =>
        new(id, code, $"Project {code}", "Riyadh", "Manager", status, start, expected);

    private static OverviewReportRow Report(
        Guid projectId, DateOnly date, string phase, string? unit, bool planMet,
        int crew = 10, string? weather = "clear", bool delay = false, string? cause = null,
        bool problem = false, string? description = null) =>
        new(projectId, date, phase, unit, crew, weather, delay, cause, problem, description, planMet);

    private static ExecutiveOverviewDto Calculate(
        IReadOnlyCollection<OverviewProjectRow> projects,
        IReadOnlyCollection<OverviewReportRow> reports,
        IReadOnlyCollection<OverviewStockRow>? stock = null,
        Guid? selected = null,
        string? phase = null)
    {
        // Progress has its own tests (StageOverviewTests); these cover the other panels.
        return ExecutiveOverviewCalculator.Calculate(
            projects, new Dictionary<Guid, Platform.Application.StageProgress.ProjectStageData>(), reports,
            stock ?? Array.Empty<OverviewStockRow>(), AsOf, selected, phase);
    }

    // --- work complete ---------------------------------------------------------------------

    [Fact]
    public void TimeElapsed_IsNull_WhenADateIsMissingOrTheSpanIsNotPositive()
    {
        ExecutiveOverviewCalculator.ComputeTimeElapsed(null, AsOf, AsOf).Should().BeNull();
        ExecutiveOverviewCalculator.ComputeTimeElapsed(AsOf, null, AsOf).Should().BeNull();
        ExecutiveOverviewCalculator.ComputeTimeElapsed(AsOf, AsOf, AsOf).Should().BeNull();
        ExecutiveOverviewCalculator.ComputeTimeElapsed(AsOf, AsOf.AddDays(-5), AsOf).Should().BeNull();
    }

    [Fact]
    public void TimeElapsed_IsClampedBetweenZeroAndOne()
    {
        ExecutiveOverviewCalculator.ComputeTimeElapsed(AsOf.AddDays(10), AsOf.AddDays(20), AsOf).Should().Be(0d);
        ExecutiveOverviewCalculator.ComputeTimeElapsed(AsOf.AddDays(-20), AsOf.AddDays(-10), AsOf).Should().Be(1d);
        ExecutiveOverviewCalculator.ComputeTimeElapsed(AsOf.AddDays(-5), AsOf.AddDays(15), AsOf).Should().BeApproximately(0.25, 1e-9);
    }

    [Theory]
    [InlineData("active", 0.60, 0.50, "ahead", 10)]
    [InlineData("active", 0.31, 0.56, "behind", -25)]
    [InlineData("active", 0.55, 0.50, "on_track", 5)]
    [InlineData("active", 0.45, 0.50, "on_track", -5)]
    [InlineData("on_hold", 0.60, 0.46, "ahead", 14)]
    [InlineData("completed", 1.00, 0.95, "finished", 5)]
    public void Schedule_ComparesWorkCompleteWithTimeElapsed(
        string status, double workComplete, double timeElapsed, string expectedStatus, int expectedGap)
    {
        var (scheduleStatus, gap) = ExecutiveOverviewCalculator.ComputeSchedule(status, workComplete, timeElapsed);

        scheduleStatus.Should().Be(expectedStatus);
        gap.Should().Be(expectedGap);
    }

    [Fact]
    public void Schedule_IsUnknown_WithoutDates()
    {
        var (scheduleStatus, gap) = ExecutiveOverviewCalculator.ComputeSchedule("active", 0.4, null);

        scheduleStatus.Should().Be("unknown");
        gap.Should().BeNull();
    }

    // --- which projects appear -------------------------------------------------------------

    [Fact]
    public void Projects_WithoutAnyReport_AreCountedButNotListed()
    {
        var result = Calculate(
            new[] { Project(ProjectA, "A"), Project(ProjectB, "B") },
            new[] { Report(ProjectA, AsOf, "excavation", Zone1, true) });

        result.Projects.Select(p => p.Code).Should().Equal("A");
        result.ProjectsWithoutReports.Should().Be(1);
        result.Tiles.ProjectCount.Should().Be(1);
    }

    [Fact]
    public void SelectingAProject_NarrowsTheFigures_ButStillListsEveryReportingProject()
    {
        var result = Calculate(
            new[] { Project(ProjectA, "A"), Project(ProjectB, "B", status: "on_hold") },
            new[]
            {
                Report(ProjectA, AsOf, "excavation", Zone1, planMet: true, crew: 10, delay: true, cause: "Late delivery"),
                Report(ProjectB, AsOf, "excavation", Zone2, planMet: false, crew: 7),
            },
            selected: ProjectB);

        result.Projects.Select(p => p.Code).Should().Equal("A", "B");
        result.Tiles.ProjectCount.Should().Be(2);
        result.Tiles.OnHoldCount.Should().Be(1);
        result.Tiles.WorkersOnSite.Should().Be(7);
        result.Tiles.PlanMetLast14Days.Should().Be(0d);
        result.DelayedReportCount.Should().Be(0);
        result.DelayCauses.Should().BeEmpty();
    }

    [Fact]
    public void Tiles_UseTheLast14Days_ThePrevious14_AndTheLast30()
    {
        var result = Calculate(
            new[] { Project(ProjectA, "A") },
            new[]
            {
                Report(ProjectA, AsOf, "excavation", Zone1, planMet: true),
                Report(ProjectA, AsOf.AddDays(-13), "excavation", Zone1, planMet: false, delay: true),
                Report(ProjectA, AsOf.AddDays(-14), "excavation", Zone2, planMet: true),            // previous window
                Report(ProjectA, AsOf.AddDays(-29), "excavation", Zone2, planMet: true, delay: true), // last 30 only
                Report(ProjectA, AsOf.AddDays(-30), "excavation", Zone2, planMet: true, delay: true), // outside 30
            });

        result.Tiles.ReportsLast14Days.Should().Be(2);
        result.Tiles.PlanMetLast14Days.Should().Be(0.5);
        result.Tiles.PlanMetPrevious14Days.Should().Be(1d);
        result.Tiles.ReportsLast30Days.Should().Be(4);
        result.Tiles.DelayedReportsLast30Days.Should().Be(2);
        result.Tiles.DelayedShareLast30Days.Should().Be(0.5);
    }

    [Fact]
    public void Tiles_ReportNothing_RatherThanZeroPercent_WhenAWindowHasNoReports()
    {
        var result = Calculate(
            new[] { Project(ProjectA, "A") },
            new[] { Report(ProjectA, AsOf.AddDays(-60), "excavation", Zone1, true) });

        result.Tiles.PlanMetLast14Days.Should().BeNull();
        result.Tiles.PlanMetPrevious14Days.Should().BeNull();
        result.Tiles.DelayedShareLast30Days.Should().BeNull();
        result.Tiles.WorkersOnSiteDate.Should().Be(AsOf.AddDays(-60));
    }

    [Fact]
    public void WorkersOnSite_AddsUpEveryReportOnTheLatestReportingDay()
    {
        var result = Calculate(
            new[] { Project(ProjectA, "A"), Project(ProjectB, "B") },
            new[]
            {
                Report(ProjectA, AsOf.AddDays(-1), "excavation", Zone1, true, crew: 99),
                Report(ProjectA, AsOf, "excavation", Zone1, true, crew: 12),
                Report(ProjectA, AsOf, "mep", "hvac@floor_1", true, crew: 8),
                Report(ProjectB, AsOf, "excavation", Zone2, true, crew: 5),
            });

        result.Tiles.WorkersOnSite.Should().Be(25);
        result.Tiles.WorkersOnSiteDate.Should().Be(AsOf);
    }

    // --- weeks -----------------------------------------------------------------------------

    [Fact]
    public void Weeks_StartOnSaturday()
    {
        ExecutiveOverviewCalculator.WeekStart(new DateOnly(2026, 10, 10)).Should().Be(new DateOnly(2026, 10, 10)); // Saturday
        ExecutiveOverviewCalculator.WeekStart(new DateOnly(2026, 10, 9)).Should().Be(new DateOnly(2026, 10, 3));   // Friday
        ExecutiveOverviewCalculator.WeekStart(new DateOnly(2026, 10, 11)).Should().Be(new DateOnly(2026, 10, 10)); // Sunday
    }

    [Fact]
    public void Weeks_LeaveOffTheCurrentWeek_UntilItIsTwoDaysOld()
    {
        var reports = new[] { Report(ProjectA, new DateOnly(2026, 9, 28), "excavation", Zone1, true) };

        ExecutiveOverviewCalculator.BuildWeeks(reports, new DateOnly(2026, 10, 10)).Should()
            .Equal(new DateOnly(2026, 9, 26), new DateOnly(2026, 10, 3));
        ExecutiveOverviewCalculator.BuildWeeks(reports, new DateOnly(2026, 10, 12)).Should()
            .Equal(new DateOnly(2026, 9, 26), new DateOnly(2026, 10, 3), new DateOnly(2026, 10, 10));
    }

    [Fact]
    public void WeeklyCharts_ShowAWeekWithNoReportsAsEmpty_NotAsZeroPercent()
    {
        var result = Calculate(
            new[] { Project(ProjectA, "A") },
            new[]
            {
                Report(ProjectA, new DateOnly(2026, 9, 19), "excavation", Zone1, planMet: true, crew: 10),
                Report(ProjectA, new DateOnly(2026, 9, 19), "mep", "hvac@floor_1", planMet: false, crew: 6),
                Report(ProjectA, new DateOnly(2026, 9, 20), "excavation", Zone1, planMet: true, crew: 20),
                Report(ProjectA, new DateOnly(2026, 10, 5), "excavation", Zone2, planMet: true, crew: 4),
            });

        result.PlanMetByWeek.Select(w => w.WeekStart).Should()
            .Equal(new DateOnly(2026, 9, 19), new DateOnly(2026, 9, 26), new DateOnly(2026, 10, 3));
        result.PlanMetByWeek[0].PlanMet.Should().BeApproximately(2d / 3d, 1e-9);
        result.PlanMetByWeek[1].PlanMet.Should().BeNull();
        result.PlanMetByWeek[1].ReportCount.Should().Be(0);

        // 19 Sep had two work fronts (10 + 6 workers), 20 Sep had one (20): average of 16 and 20.
        result.WorkersByWeek[0].AverageWorkersPerDay.Should().Be(18d);
        result.WorkersByWeek[0].ReportingDays.Should().Be(2);
        result.WorkersByWeek[1].AverageWorkersPerDay.Should().Be(0d);
    }

    // --- weather, delays, problems ---------------------------------------------------------

    [Fact]
    public void Weather_LeavesOutPaperworkDays_AndConditionsWithTooFewReports()
    {
        var reports = new List<OverviewReportRow>();
        for (var i = 0; i < 5; i++)
            reports.Add(Report(ProjectA, AsOf.AddDays(-i), "excavation", Zone1, planMet: i < 4, weather: "clear"));
        for (var i = 0; i < 4; i++)
            reports.Add(Report(ProjectA, AsOf.AddDays(-i), "excavation", Zone2, planMet: false, weather: "sandstorm"));
        for (var i = 0; i < 9; i++)
            reports.Add(Report(ProjectA, AsOf.AddDays(-i), "paperwork", "design_approval", planMet: false, weather: "clear"));

        var result = Calculate(new[] { Project(ProjectA, "A") }, reports);

        var weather = result.PlanMetByWeather.Single();
        weather.Weather.Should().Be("clear");
        weather.ReportCount.Should().Be(5);
        weather.PlanMet.Should().BeApproximately(0.8, 1e-9);
    }

    [Fact]
    public void DelayCauses_AreCountedMostCommonFirst_AndCappedAtSeven()
    {
        var reports = new List<OverviewReportRow>();
        for (var cause = 1; cause <= 9; cause++)
            for (var n = 0; n < cause; n++)
                reports.Add(Report(ProjectA, AsOf.AddDays(-n), "excavation", Zone1, false, delay: true, cause: $"Cause {cause}"));
        reports.Add(Report(ProjectA, AsOf, "excavation", Zone1, false, delay: true, cause: null));

        var result = Calculate(new[] { Project(ProjectA, "A") }, reports);

        result.DelayedReportCount.Should().Be(46, "a delayed report with no cause written still counts as delayed");
        result.DelayCauses.Should().HaveCount(7);
        result.DelayCauses[0].Should().Be(new OverviewLabelCountDto("Cause 9", 9));
        result.DelayCauses[6].Should().Be(new OverviewLabelCountDto("Cause 3", 3));
    }

    [Fact]
    public void LatestProblems_AreTheSixMostRecent_NewestFirst()
    {
        var reports = Enumerable.Range(0, 8)
            .Select(i => Report(ProjectA, AsOf.AddDays(-i), "excavation", Zone1, true, problem: true, description: $"Problem {i}"))
            .Append(Report(ProjectA, AsOf, "excavation", Zone2, true, problem: false, description: null))
            .ToList();

        var result = Calculate(new[] { Project(ProjectA, "A") }, reports);

        result.ProblemReportCount.Should().Be(8);
        result.LatestProblems.Select(p => p.Description).Should()
            .Equal("Problem 0", "Problem 1", "Problem 2", "Problem 3", "Problem 4", "Problem 5");
        result.LatestProblems[0].ProjectCode.Should().Be("A");
    }

    // --- stock -----------------------------------------------------------------------------

    [Fact]
    public void Stock_AddsUpReceivedAndIssuedPerMaterial_AndKeepsFreeTextMaterialsSeparate()
    {
        var stock = new[]
        {
            new OverviewStockRow(ProjectA, AsOf, true, "cement", false, 100m, "bag", null),
            new OverviewStockRow(ProjectA, AsOf, true, "cement", false, 50m, "bag", null),
            new OverviewStockRow(ProjectA, AsOf, false, "cement", false, 60m, "bag", "used_in_construction"),
            new OverviewStockRow(ProjectA, AsOf, false, "cement", false, 15m, "bag", "damaged_or_wasted"),
            new OverviewStockRow(ProjectA, AsOf, true, "Cables", true, 20m, "roll", null),
            new OverviewStockRow(ProjectB, AsOf, true, "rebar", false, 8.5m, "ton", null),
        };

        var result = Calculate(
            new[] { Project(ProjectA, "A"), Project(ProjectB, "B") },
            new[] { Report(ProjectA, AsOf, "excavation", Zone1, true), Report(ProjectB, AsOf, "excavation", Zone2, true) },
            stock);

        result.StockByMaterial.Select(m => m.Material).Should().Equal("cement", "rebar", "Cables");
        var cement = result.StockByMaterial[0];
        cement.Received.Should().Be(150d);
        cement.Issued.Should().Be(75d);
        cement.OnHand.Should().Be(75d);
        cement.IssuedShare.Should().Be(0.5);
        cement.Unit.Should().Be("bag");
        result.StockByMaterial[2].IsCustomMaterial.Should().BeTrue();

        result.StockIssueCount.Should().Be(2);
        result.IssueReasons.Should().Equal(
            new OverviewLabelCountDto("used_in_construction", 1), new OverviewLabelCountDto("damaged_or_wasted", 1));
    }

    [Fact]
    public void Stock_FollowsTheSelectedProject()
    {
        var stock = new[]
        {
            new OverviewStockRow(ProjectA, AsOf, true, "cement", false, 100m, "bag", null),
            new OverviewStockRow(ProjectB, AsOf, true, "rebar", false, 8.5m, "ton", null),
        };

        var result = Calculate(
            new[] { Project(ProjectA, "A"), Project(ProjectB, "B") },
            new[] { Report(ProjectA, AsOf, "excavation", Zone1, true), Report(ProjectB, AsOf, "excavation", Zone2, true) },
            stock, selected: ProjectB);

        result.StockByMaterial.Select(m => m.Material).Should().Equal("rebar");
    }

    // --- lateness and forecast -------------------------------------------------------------

    [Fact]
    public void DaysLate_CountsDaysWhereNothingInScopeMetItsPlan_AndFollowsTheStage()
    {
        var reports = new[]
        {
            // Day 1: both fronts missed -> late. Day 2: one front on plan -> not late.
            Report(ProjectA, AsOf.AddDays(-2), "excavation", Zone1, planMet: false),
            Report(ProjectA, AsOf.AddDays(-2), "paperwork", "design_approval", planMet: false),
            Report(ProjectA, AsOf.AddDays(-1), "excavation", Zone1, planMet: false),
            Report(ProjectA, AsOf.AddDays(-1), "paperwork", "design_approval", planMet: true),
            Report(ProjectA, AsOf, "paperwork", "design_approval", planMet: true),
        };

        var all = Calculate(new[] { Project(ProjectA, "A") }, reports).Tiles;
        all.LateDays.Should().Be(1);
        all.WorkedDays.Should().Be(3);
        all.LateShare.Should().BeApproximately(1d / 3d, 1e-9);
        all.LateShareTone.Should().Be("bad");
        all.LateDaysTone.Should().Be("good");

        var excavation = Calculate(new[] { Project(ProjectA, "A") }, reports, phase: "excavation").Tiles;
        excavation.LateDays.Should().Be(2);
        excavation.WorkedDays.Should().Be(2);
        // Stage days come from the project's stage plan (none in this test): see StageOverviewTests.
        excavation.StageEarnedDays.Should().Be(0);
        excavation.StagePlannedDays.Should().Be(0);
    }

    [Theory]
    [InlineData(0, "good")]
    [InlineData(30, "warn")]
    [InlineData(31, "bad")]
    public void ForecastTone_TurnsAmberThenRed(int daysLate, string tone) =>
        OverviewKpiThresholds.ForecastTone(daysLate).Should().Be(tone);

    [Theory]
    [InlineData(5, "good")]
    [InlineData(15, "warn")]
    [InlineData(16, "bad")]
    public void LateDaysTone_TurnsAmberThenRed(int lateDays, string tone) =>
        OverviewKpiThresholds.LateDaysTone(lateDays).Should().Be(tone);

    [Fact]
    public void AddWorkingDays_SkipsFriday()
    {
        // Thursday 15 Oct 2026 + 1 working day = Saturday 17 Oct.
        ExecutiveOverviewCalculator.AddWorkingDays(new DateOnly(2026, 10, 15), 1).Should().Be(new DateOnly(2026, 10, 17));
    }

    // --- nothing at all --------------------------------------------------------------------

    [Fact]
    public void AnEmptySystem_ProducesAnEmptyPage_NotAnError()
    {
        var result = Calculate(Array.Empty<OverviewProjectRow>(), Array.Empty<OverviewReportRow>());

        result.Projects.Should().BeEmpty();
        result.PlanMetByWeek.Should().BeEmpty();
        result.DataFrom.Should().BeNull();
        result.Tiles.WorkComplete.Should().Be(0d);
        result.Tiles.WorkersOnSite.Should().BeNull();
    }
}
