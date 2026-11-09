using FluentAssertions;
using Platform.Application.StageProgress;
using Xunit;

namespace Platform.Infrastructure.IntegrationTests.StageProgress;

public class StageEligibilityTests
{
    private static readonly DateOnly Day = new(2026, 11, 8);
    private readonly ProjectShape _shape = StageScheduleTests.Reference();
    private readonly PlannedSchedule _schedule;

    public StageEligibilityTests() => _schedule = StageScheduleBuilder.Build(DefaultStagePlan.Rows, _shape);

    private Guid L(int n) => _shape.ActiveLevels.Single(l => l.LevelNumber == n).Id;

    private static StageReportBlock B(string stage, string? substep = null, Guid[]? levels = null, int[]? sides = null, bool met = true, bool finished = false, DateOnly? date = null) =>
        new(Guid.NewGuid(), date ?? Day, stage, substep, levels ?? [], sides ?? [], met, finished);

    private IReadOnlyList<StageProblem> Check(IReadOnlyList<StageReportBlock> incoming, IReadOnlyList<StageReportBlock>? earlier = null, IReadOnlyList<StageBaseline>? baselines = null, IReadOnlyList<StageReopening>? reopenings = null) =>
        StageEligibility.Check(_schedule, incoming, StageProgressCalculator.Compute(_schedule.Units, earlier ?? [], baselines, reopenings));

    [Fact]
    public void ValidBlocks_PassInOneReport_StructureAndWallsTogether()
    {
        Check([B(StageCodes.Structure, levels: [L(4)]), B(StageCodes.Walls, levels: [L(2)])]).Should().BeEmpty();
    }

    [Fact]
    public void TheSameStageTwiceInOneReport_IsRefused()
    {
        Check([B("plastering_floors", levels: [L(1)]), B("plastering_floors", levels: [L(2)])])
            .Should().ContainSingle().Which.Should().Match<StageProblem>(p => p.Code == StageEligibility.Duplicate && p.BlockIndex == 1);
    }

    [Fact]
    public void StructureNeedsExactlyOneLevel_AndPerFloorStagesNeedAFloorInScope()
    {
        Check([B(StageCodes.Structure, levels: [L(1), L(2)])]).Should().ContainSingle(p => p.Code == StageEligibility.UnitsNeeded);
        Check([B("paint_1")]).Should().ContainSingle(p => p.Code == StageEligibility.UnitsNeeded && p.Field == "floors");
        Check([B("paint_1", levels: [L(0)])]).Should().ContainSingle(p => p.Code == StageEligibility.UnitsNeeded, "paint is apartment floors only");
        Check([B("plastering_outside", sides: [5])]).Should().ContainSingle(p => p.Code == StageEligibility.UnitsNeeded, "the project has 4 sides");
    }

    [Fact]
    public void AStageThatDoesNotApply_IsNotInThePlan()
    {
        Check([B("deconstruction")]).Should().ContainSingle(p => p.Code == StageEligibility.NotInPlan);
    }

    [Fact]
    public void AStageWithSubsteps_NeedsOneOfThem()
    {
        Check([B("aluminium", levels: [L(1)])]).Should().ContainSingle(p => p.Code == StageEligibility.SubstepNeeded);
        Check([B("aluminium", "nonsense", levels: [L(1)])]).Should().ContainSingle(p => p.Code == StageEligibility.SubstepNeeded);
    }

    [Fact]
    public void AFinishedUnit_CantBeReportedAgain_UntilReopened()
    {
        var earlier = new[] { B(StageCodes.Structure, levels: [L(3)], finished: true, date: Day.AddDays(-2)) };
        Check([B(StageCodes.Structure, levels: [L(3)])], earlier).Should().ContainSingle(p => p.Code == StageEligibility.UnitFinished);

        var reopened = new[] { new StageReopening(UnitKeys.For(StageCodes.Structure, null, L(3), null), Day.AddDays(-1)) };
        Check([B(StageCodes.Structure, levels: [L(3)])], earlier, reopenings: reopened).Should().BeEmpty();
    }

    [Fact]
    public void AUnitFinishedInTheStartingPosition_CantBeReportedAgain()
    {
        var baseline = new[] { new StageBaseline("foundation", null, null, null, 0, true) };
        Check([B("foundation")], baselines: baseline).Should().ContainSingle(p => p.Code == StageEligibility.UnitFinished);
    }

    [Fact]
    public void SubstepsStartInOrder_PerFloor()
    {
        // Glass fixing on floor 2 before window frames started there: refused.
        Check([B("aluminium", "glass_fixing", levels: [L(2)])]).Should().ContainSingle(p => p.Code == StageEligibility.SubstepOrder);

        // Window frames started on floor 2 in an earlier report: fine there, still refused on floor 3.
        var earlier = new[] { B("aluminium", "window_frames", levels: [L(2)], met: false, date: Day.AddDays(-1)) };
        Check([B("aluminium", "glass_fixing", levels: [L(2)])], earlier).Should().BeEmpty();
        Check([B("aluminium", "glass_fixing", levels: [L(3)])], earlier).Should().ContainSingle(p => p.Code == StageEligibility.SubstepOrder);

        // Both in the same report, the earlier sub-step first: fine.
        Check([B("aluminium", "window_frames", levels: [L(4)]), B("aluminium", "glass_fixing", levels: [L(4)])]).Should().BeEmpty();
    }

    [Fact]
    public void FixedStageSubsteps_StartInOrder_ForTheWholeProject()
    {
        Check([B("paperwork_1", "area_order")]).Should().ContainSingle(p => p.Code == StageEligibility.SubstepOrder);
        Check([B("paperwork_1", "area_order")], [B("paperwork_1", "project_data", date: Day.AddDays(-3))]).Should().BeEmpty();
    }
}
