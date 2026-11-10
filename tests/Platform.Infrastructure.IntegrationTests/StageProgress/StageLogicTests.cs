using FluentAssertions;
using Platform.Application.StageProgress;
using Xunit;

namespace Platform.Infrastructure.IntegrationTests.StageProgress;

/// <summary>Logic cases beyond the reference project (rigorous pass, 2026-11-10).</summary>
public class StageLogicTests
{
    private static readonly DateOnly Day = new(2026, 11, 8);

    private static StageReportBlock B(string stage, string? substep, Guid[]? levels = null, bool met = true, bool finished = false, DateOnly? date = null, DateTime? saved = null) =>
        new(Guid.NewGuid(), date ?? Day, stage, substep, levels ?? [], [], met, finished, saved);

    [Fact]
    public void TwoParkingLevels_SevenLevels_StructureAndWallsKeepTheOverlapRule()
    {
        var shape = StageScheduleTests.Reference(parking: 2, floors: 5);
        var s = StageScheduleBuilder.Build(DefaultStagePlan.Rows, shape);
        var begin = s.Stages.Single(x => x.StageCode == StageCodes.Structure).Window.Fastest.Start;
        var walls = s.Stages.Single(x => x.StageCode == StageCodes.Walls).Window;
        // 7 levels: structure 70; walls start at 21,31,...,61 then 71 (after all structure), 80; end 88.
        (walls.Fastest.End - begin).Should().Be(88);
        var starts = shape.ActiveLevels.Select(l => s.UnitWindows[UnitKeys.For(StageCodes.Walls, null, l.Id, null)].Fastest.Start - begin + 1).ToList();
        starts.Should().Equal(21, 31, 41, 51, 61, 71, 80);
        shape.ActiveLevels.First().LevelNumber.Should().Be(-1, "parking levels are numbered downwards, deepest first");
        s.Units.Count(u => u.StageCode == "paint_1").Should().Be(5, "parking is never painted");
    }

    [Fact]
    public void NoParking_AllLevelsAreApartmentFloors_AndParkingOnlyStagesHaveNoUnits()
    {
        var shape = StageScheduleTests.Reference(parking: 0, floors: 5);
        var plan = DefaultStagePlan.Rows.Append(new StagePlanRow(365, "parking_paint", "Parking paint", "دهان المواقف", null, null, null, 3, DurationType.PerFloor, LevelScope.ParkingOnly, AppliesWhen.Always));
        var s = StageScheduleBuilder.Build(plan, shape);
        s.Units.Count(u => u.StageCode == StageCodes.Structure).Should().Be(5);
        s.Units.Should().NotContain(u => u.StageCode == "parking_paint");
        (s.Stages.Single(x => x.StageCode == StageCodes.Walls).Window.Fastest.End - s.Stages.Single(x => x.StageCode == StageCodes.Structure).Window.Fastest.Start).Should().Be(50 + 9 + 9 - 9 + 9 - 9 + 9);
    }

    [Fact]
    public void ChangingDemolitionLater_BringsTheStageIntoTheSchedule()
    {
        var before = StageScheduleBuilder.Build(DefaultStagePlan.Rows, StageScheduleTests.Reference());
        var after = StageScheduleBuilder.Build(DefaultStagePlan.Rows, StageScheduleTests.Reference(deconstruction: true));
        (after.Project.Fastest.End - before.Project.Fastest.End).Should().Be(14);
        after.Units.Should().Contain(u => u.StageCode == "deconstruction");
    }

    [Fact]
    public void ADuplicatePlanRow_IsIgnored_InsteadOfBreakingEveryReport()
    {
        var shape = StageScheduleTests.Reference();
        var plan = DefaultStagePlan.Rows.Append(DefaultStagePlan.Rows.First(r => r.StageCode == "excavation") with { SortOrder = 999 }).ToList();
        var s = StageScheduleBuilder.Build(plan, shape);
        s.PlannedUnitDays.Should().Be(575);
        var act = () => StageEligibility.Check(s, [B("excavation", null)], StageProgressCalculator.Compute(s.Units, []));
        act.Should().NotThrow();
    }

    [Fact]
    public void ReopenedAndFinishedAgainOnTheSameDay_CountsAsFinished()
    {
        var s = StageScheduleBuilder.Build(DefaultStagePlan.Rows, StageScheduleTests.Reference());
        var key = UnitKeys.For("excavation", null, null, null);
        var morning = new DateTime(2026, 11, 8, 6, 0, 0, DateTimeKind.Utc);
        var blocks = new[]
        {
            B("excavation", null, finished: true, date: Day.AddDays(-2), saved: morning.AddDays(-2)),
            B("excavation", null, finished: true, date: Day, saved: morning.AddHours(3)),
        };
        var reopened = new[] { new StageReopening(key, Day, morning) };
        var u = StageProgressCalculator.Compute(s.Units, blocks, reopenings: reopened).Single(x => x.Unit.Key == key);
        u.Finished.Should().BeTrue("the second finish was saved after the reopening");
        u.ActualFinish.Should().Be(Day);

        var onlyOld = StageProgressCalculator.Compute(s.Units, blocks.Take(1), reopenings: reopened).Single(x => x.Unit.Key == key);
        onlyOld.Finished.Should().BeFalse("the first finish was saved before the reopening");
    }

    [Fact]
    public void ALaterStepListedBeforeItsEarlierStepInTheSameReport_IsAccepted()
    {
        var shape = StageScheduleTests.Reference();
        var s = StageScheduleBuilder.Build(DefaultStagePlan.Rows, shape);
        var floor = shape.ActiveLevels.Single(l => l.LevelNumber == 3).Id;
        StageEligibility.Check(s,
            [B("aluminium", "glass_fixing", [floor]), B("aluminium", "window_frames", [floor])],
            StageProgressCalculator.Compute(s.Units, [])).Should().BeEmpty();
    }

    [Fact]
    public void ElapsedTime_RunsFromTheFirstReport_WhenThereIsNoStartingPosition()
    {
        var shape = StageScheduleTests.Reference();
        var start = new DateOnly(2026, 10, 1);
        var first = new DateOnly(2026, 11, 1);
        var data = new ProjectStageData(new StageProject(Guid.NewGuid(), "N", "N", "active", start, 1, 5, shape), DefaultStagePlan.Rows,
            [new StageBlockRow(Guid.NewGuid(), Guid.NewGuid(), B("paperwork_1", "project_data", date: first))], [], [], []);
        var p = ProjectProgressCalculator.Compute(data, new DateOnly(2026, 11, 10));
        p.ElapsedWorkingDays.Should().Be(new WorkingCalendar().WorkingDaysBetween(first, new DateOnly(2026, 11, 10)));
        p.FinishTarget.Should().Be(new StageDates(start, new WorkingCalendar()).Finish(p.Schedule.Project.Middle.End), "planned dates still run from the planned start");
    }

    [Fact]
    public void PlannedShare_NeverFalls_AsTimePasses()
    {
        var s = StageScheduleBuilder.Build(DefaultStagePlan.Rows, StageScheduleTests.Reference());
        var shares = Enumerable.Range(0, 700).Select(d => StageProgressCalculator.PlannedShare(s, d, StageDates.Slack)).ToList();
        shares.Should().BeInAscendingOrder();
        shares[^1].Should().BeApproximately(1, 1e-9);
    }

    [Fact]
    public void EarnedDays_NeverExceedPlanned_AndNeverGoNegative()
    {
        var shape = StageScheduleTests.Reference();
        var s = StageScheduleBuilder.Build(DefaultStagePlan.Rows, shape);
        var all = shape.ActiveLevels.Select(l => l.Id).ToArray();
        var blocks = Enumerable.Range(0, 40).Select(i => B("plastering_floors", null, all, date: Day.AddDays(i))).ToList();
        var units = StageProgressCalculator.Compute(s.Units, blocks, [new StageBaseline("plastering_floors", null, all[1], null, 99, false)]);
        units.Should().OnlyContain(u => u.EarnedDays >= 0 && u.EarnedDays <= u.Unit.PlannedDays);
    }

    [Fact]
    public void AResumeWithoutAPause_AndAPauseTwice_DoNotDoubleCount()
    {
        var spans = PauseSpans.From([
            new PauseEvent(new DateOnly(2026, 3, 1), PauseAction.Resume, 1),
            new PauseEvent(new DateOnly(2026, 3, 2), PauseAction.Pause, 2),
            new PauseEvent(new DateOnly(2026, 3, 4), PauseAction.Pause, 3),
            new PauseEvent(new DateOnly(2026, 3, 9), PauseAction.Resume, 4),
        ], new DateOnly(2026, 11, 10));
        spans.Should().Equal(new DateSpan(new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 8)));
    }
}
