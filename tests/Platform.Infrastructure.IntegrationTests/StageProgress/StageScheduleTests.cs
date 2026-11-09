using FluentAssertions;
using Platform.Application.StageProgress;
using Xunit;

namespace Platform.Infrastructure.IntegrationTests.StageProgress;

/// <summary>
/// The planned schedule built from the default stage plan, checked against the reference
/// totals agreed with the owner (2026-11-10) for the reference project: 1 parking level +
/// 5 apartment floors, 4 outside sides.
/// </summary>
public class StageScheduleTests
{
    internal static ProjectShape Reference(bool deconstruction = false, bool support = false, int parking = 1, int floors = 5, int sides = 4)
    {
        var levels = new List<ProjectLevel>();
        for (var p = parking - 1; p >= 0; p--) levels.Add(new(Guid.NewGuid(), -p, LevelKind.Parking, "المواقف", "Parking"));
        for (var f = 1; f <= floors; f++) levels.Add(new(Guid.NewGuid(), f, LevelKind.Apartment, $"الدور {f}", $"Floor {f}"));
        return new ProjectShape(levels, sides, deconstruction, support);
    }

    private static PlannedSchedule Build(ProjectShape shape) => StageScheduleBuilder.Build(DefaultStagePlan.Rows, shape);

    [Fact]
    public void TheDefaultPlan_HasThe36SeedRows_InOrder()
    {
        DefaultStagePlan.Rows.Should().HaveCount(36);
        DefaultStagePlan.Rows.Select(r => r.SortOrder).Should().BeInAscendingOrder().And.OnlyHaveUniqueItems();
        DefaultStagePlan.Rows.Should().OnlyContain(r => r.Days > 0 && r.StageNameEn != "" && r.StageNameAr != "");
        DefaultStagePlan.Rows.Where(r => r.SubstepCode is not null).Should().OnlyContain(r => r.SubstepNameEn != null && r.SubstepNameAr != null);
    }

    [Fact]
    public void SkeletonReady_Is170_WithoutDemolitionOrShoring_And194_WithBoth()
    {
        Build(Reference()).Skeleton!.Value.Fastest.End.Should().Be(170);
        Build(Reference()).Skeleton!.Value.Slowest.End.Should().Be(170);
        Build(Reference(deconstruction: true, support: true)).Skeleton!.Value.Fastest.End.Should().Be(194);
        Build(Reference(deconstruction: true, support: true)).Skeleton!.Value.Slowest.End.Should().Be(194);
    }

    [Fact]
    public void Finishing_Takes158DaysFastest_And369Slowest()
    {
        foreach (var shape in new[] { Reference(), Reference(deconstruction: true, support: true) })
        {
            var s = Build(shape);
            (s.Project.Fastest.End - s.Skeleton!.Value.Fastest.End).Should().Be(158);
            (s.Project.Slowest.End - s.Skeleton!.Value.Slowest.End).Should().Be(369);
        }
    }

    [Fact]
    public void TheWholeProject_Is328To563WorkingDays()
    {
        Build(Reference()).Project.Fastest.End.Should().Be(328);
        Build(Reference(deconstruction: true, support: true)).Project.Slowest.End.Should().Be(563);
        Build(Reference()).Project.Slowest.End.Should().Be(539);
        Build(Reference(deconstruction: true, support: true)).Project.Fastest.End.Should().Be(352);
    }

    [Fact]
    public void WithTheTenPercentSlack_TheTotalsAre187_214_174_406_361_620()
    {
        StageDates.WithSlack(170).Should().Be(187);
        StageDates.WithSlack(194).Should().Be(214);
        StageDates.WithSlack(158).Should().Be(174);
        StageDates.WithSlack(369).Should().Be(406);
        StageDates.WithSlack(328).Should().Be(361);
        StageDates.WithSlack(563).Should().Be(620);
    }

    [Fact]
    public void StructureAndWalls_Overlap_Into78Days_WithWallsStartingOnDays21To70()
    {
        var shape = Reference();
        var s = Build(shape);
        var structure = s.Stages.Single(x => x.StageCode == StageCodes.Structure);
        var walls = s.Stages.Single(x => x.StageCode == StageCodes.Walls);
        var begin = structure.Window.Fastest.Start; // 92 = paperwork 60 + excavation 4 + foundation 28
        begin.Should().Be(92);
        structure.Window.Fastest.End.Should().Be(begin + 60);
        walls.Window.Fastest.End.Should().Be(begin + 78);
        walls.Window.Slowest.End.Should().Be(begin + 78);

        var wallStarts = shape.ActiveLevels
            .Select(l => s.UnitWindows[UnitKeys.For(StageCodes.Walls, null, l.Id, null)].Fastest.Start - begin + 1)
            .ToList();
        wallStarts.Should().Equal(21, 31, 41, 51, 61, 70);

        var structureStarts = shape.ActiveLevels
            .Select(l => s.UnitWindows[UnitKeys.For(StageCodes.Structure, null, l.Id, null)].Fastest.Start - begin + 1)
            .ToList();
        structureStarts.Should().Equal(1, 11, 21, 31, 41, 51);

        // The next stage starts once walls finish.
        s.Stages.Single(x => x.StageCode == "plastering_parking").Window.Fastest.Start.Should().Be(begin + 78);
    }

    [Fact]
    public void Units_CoverOnlyTheLevelsInScope_AndOnlyStagesThatApply()
    {
        var shape = Reference();
        var s = Build(shape);
        s.PlannedUnitDays.Should().Be(575);
        s.Units.Should().NotContain(u => u.StageCode == "deconstruction" || u.StageCode == "support");
        s.Units.Count(u => u.StageCode == StageCodes.Structure).Should().Be(6);
        s.Units.Count(u => u.StageCode == "paint_1").Should().Be(5, "apartment floors only");
        s.Units.Count(u => u.StageCode == "plastering_outside").Should().Be(4, "one per side");

        Build(Reference(deconstruction: true, support: true)).PlannedUnitDays.Should().Be(575 + 24);
    }

    [Fact]
    public void AnInactiveRow_DropsOutOfTheSchedule()
    {
        var plan = DefaultStagePlan.Rows.Select(r => r.StageCode == "stair_railings" ? r with { IsActive = false } : r).ToList();
        StageScheduleBuilder.Build(plan, Reference()).Project.Fastest.End.Should().Be(318);
    }

    [Fact]
    public void ADeactivatedFloor_HasNoUnits()
    {
        var shape = Reference();
        var levels = shape.Levels.Select((l, i) => i == 5 ? l with { IsActive = false } : l).ToList();
        var fewer = new ProjectShape(levels, 4, false, false);
        Build(fewer).Units.Count(u => u.StageCode == StageCodes.Structure).Should().Be(5);
    }

    [Fact]
    public void SlowestPerFloorStage_RunsOneFloorAtATime_WithAllSubstepsOnAFloorFirst()
    {
        var shape = Reference();
        var s = Build(shape);
        var stage = s.Stages.Single(x => x.StageCode == "aluminium");
        (stage.Window.Fastest.End - stage.Window.Fastest.Start).Should().Be(6);
        (stage.Window.Slowest.End - stage.Window.Slowest.Start).Should().Be(30);
        var floor2 = shape.ActiveLevels.Single(l => l.LevelNumber == 2).Id;
        var glass = s.UnitWindows[UnitKeys.For("aluminium", "glass_fixing", floor2, null)];
        glass.Slowest.Start.Should().Be(stage.Window.Slowest.Start + 6 + 3);
        glass.Fastest.Start.Should().Be(stage.Window.Fastest.Start + 3);
    }
}
