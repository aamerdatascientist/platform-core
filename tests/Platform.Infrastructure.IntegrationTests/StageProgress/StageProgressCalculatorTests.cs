using FluentAssertions;
using Platform.Application.StageProgress;
using Xunit;

namespace Platform.Infrastructure.IntegrationTests.StageProgress;

public class StageProgressCalculatorTests
{
    private static readonly DateOnly Sat = new(2026, 11, 7); // a Saturday

    private readonly ProjectShape _shape = StageScheduleTests.Reference();
    private readonly PlannedSchedule _schedule;

    public StageProgressCalculatorTests() => _schedule = StageScheduleBuilder.Build(DefaultStagePlan.Rows, _shape);

    private Guid Level(int n) => _shape.ActiveLevels.Single(l => l.LevelNumber == n).Id;

    private static StageReportBlock Block(DateOnly date, string stage, string? substep = null, Guid[]? levels = null, int[]? sides = null,
        bool met = true, bool finished = false, Guid? submission = null) =>
        new(submission ?? Guid.NewGuid(), date, stage, substep, levels ?? [], sides ?? [], met, finished);

    private UnitProgress Unit(IReadOnlyList<UnitProgress> all, string stage, string? substep = null, Guid? level = null, int? side = null) =>
        all.Single(u => u.Unit.Key == UnitKeys.For(stage, substep, level, side));

    [Fact]
    public void EachPlanMetDay_EarnsOneDayPerSelectedFloor()
    {
        var result = StageProgressCalculator.Compute(_schedule.Units,
        [
            Block(Sat, "plastering_floors", levels: [Level(1), Level(2)]),
            Block(Sat.AddDays(1), "plastering_floors", levels: [Level(2)]),
            Block(Sat.AddDays(2), "plastering_floors", levels: [Level(1)], met: false),
        ]);
        Unit(result, "plastering_floors", level: Level(1)).EarnedDays.Should().Be(1);
        Unit(result, "plastering_floors", level: Level(2)).EarnedDays.Should().Be(2);
        Unit(result, "plastering_floors", level: Level(3)).EarnedDays.Should().Be(0);
    }

    [Fact]
    public void EarnedDays_AreCappedAtThePlannedDays()
    {
        var blocks = Enumerable.Range(0, 12).Select(i => Block(Sat.AddDays(i), "excavation")).ToList();
        Unit(StageProgressCalculator.Compute(_schedule.Units, blocks), "excavation").EarnedDays.Should().Be(4);
    }

    [Fact]
    public void TheSameUnitTwiceOnOneDay_EarnsOneDay()
    {
        var result = StageProgressCalculator.Compute(_schedule.Units,
            [Block(Sat, StageCodes.Structure, levels: [Level(3)]), Block(Sat, StageCodes.Structure, levels: [Level(3)])]);
        Unit(result, StageCodes.Structure, level: Level(3)).EarnedDays.Should().Be(1);
    }

    [Fact]
    public void OneCombinedReport_AndSeparateReports_GiveIdenticalResults()
    {
        var day = Sat.AddDays(3);
        var report = Guid.NewGuid();
        var combined = StageProgressCalculator.Compute(_schedule.Units,
        [
            Block(day, StageCodes.Structure, levels: [Level(4)], submission: report),
            Block(day, StageCodes.Walls, levels: [Level(2)], submission: report),
        ]);
        var separate = StageProgressCalculator.Compute(_schedule.Units,
        [
            Block(day, StageCodes.Structure, levels: [Level(4)]),
            Block(day, StageCodes.Walls, levels: [Level(2)]),
        ]);
        combined.Select(u => (u.Unit.Key, u.EarnedDays, u.Finished)).Should().Equal(separate.Select(u => (u.Unit.Key, u.EarnedDays, u.Finished)));
    }

    [Fact]
    public void Finished_SetsEarnedToPlanned_AndDatesTheFinish_PlanNotMetEarnsNothing()
    {
        var result = StageProgressCalculator.Compute(_schedule.Units,
        [
            Block(Sat, "foundation"),
            Block(Sat.AddDays(1), "foundation", met: false, finished: true),
        ]);
        var u = Unit(result, "foundation");
        u.EarnedDays.Should().Be(28);
        u.Finished.Should().BeTrue();
        u.ActualStart.Should().Be(Sat);
        u.ActualFinish.Should().Be(Sat.AddDays(1));
    }

    [Fact]
    public void LoweringAUnitsPlannedDays_CapsIt_AndRaisingThemAgainGivesTheDaysBack()
    {
        var blocks = Enumerable.Range(0, 7).Select(i => Block(Sat.AddDays(i), "plastering_parking")).ToList();
        StageProgressCalculator.Compute(_schedule.Units, blocks).Sum(u => u.EarnedDays).Should().Be(5);

        Unit(Recompute(3), "plastering_parking").EarnedDays.Should().Be(3);
        Unit(Recompute(10), "plastering_parking").EarnedDays.Should().Be(7);

        IReadOnlyList<UnitProgress> Recompute(int days)
        {
            var plan = DefaultStagePlan.Rows.Select(r => r.StageCode == "plastering_parking" ? r with { Days = days } : r).ToList();
            return StageProgressCalculator.Compute(StageScheduleBuilder.Build(plan, _shape).Units, blocks);
        }
    }

    [Fact]
    public void AReopenedUnit_IsNoLongerFinished_ButKeepsItsPlanMetDays()
    {
        var key = UnitKeys.For("excavation", null, null, null);
        var blocks = new[] { Block(Sat, "excavation"), Block(Sat.AddDays(1), "excavation", finished: true) };
        var result = StageProgressCalculator.Compute(_schedule.Units, blocks, reopenings: [new StageReopening(key, Sat.AddDays(2))]);
        Unit(result, "excavation").Finished.Should().BeFalse();
        Unit(result, "excavation").EarnedDays.Should().Be(2);
    }

    [Fact]
    public void ABaseline_CountsWithoutDates_AndLaterReportsAddToIt()
    {
        var baselines = new[]
        {
            new StageBaseline("foundation", null, null, null, 0, true),
            new StageBaseline(StageCodes.Structure, null, Level(4), null, 4, false),
        };
        var result = StageProgressCalculator.Compute(_schedule.Units, [Block(Sat, StageCodes.Structure, levels: [Level(4)])], baselines);
        var foundation = Unit(result, "foundation");
        foundation.Finished.Should().BeTrue();
        foundation.EarnedDays.Should().Be(28);
        foundation.ActualFinish.Should().BeNull("a baseline has no date");
        var structure = Unit(result, StageCodes.Structure, level: Level(4));
        structure.EarnedDays.Should().Be(5);
        structure.ActualStart.Should().BeNull("it started before the first report we have");
    }

    [Fact]
    public void Project119sStartingPosition_Is158Of575Days()
    {
        var baselines = new List<StageBaseline>();
        foreach (var row in DefaultStagePlan.Rows.Where(r => r.StageCode is "paperwork_1" or "paperwork_2" or "paperwork_3" or "excavation" or "foundation"))
            baselines.Add(new(row.StageCode, row.SubstepCode, null, null, 0, true));
        foreach (var n in new[] { 0, 1, 2, 3 }) baselines.Add(new(StageCodes.Structure, null, Level(n), null, 0, true));
        foreach (var n in new[] { 0, 1 }) baselines.Add(new(StageCodes.Walls, null, Level(n), null, 0, true));
        baselines.Add(new(StageCodes.Structure, null, Level(4), null, 4, false));
        baselines.Add(new(StageCodes.Walls, null, Level(2), null, 4, false));

        var result = StageProgressCalculator.Compute(_schedule.Units, [], baselines);
        result.Sum(u => u.EarnedDays).Should().Be(158);
        _schedule.PlannedUnitDays.Should().Be(575);
    }

    [Fact]
    public void PlannedShare_IsZeroAtTheStart_AndOneAtTheSlackedEnd()
    {
        StageProgressCalculator.PlannedShare(_schedule, 0, StageDates.Slack).Should().Be(0);
        var end = StageDates.WithSlack(_schedule.Project.Middle.End);
        StageProgressCalculator.PlannedShare(_schedule, end, StageDates.Slack).Should().BeApproximately(1, 1e-9);
        StageProgressCalculator.PlannedShare(_schedule, end / 2, StageDates.Slack).Should().BeInRange(0.2, 0.8);
    }
}
