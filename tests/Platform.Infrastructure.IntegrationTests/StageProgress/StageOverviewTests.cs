using FluentAssertions;
using Platform.Application.Analytics.Queries.GetExecutiveOverview;
using Platform.Application.StageProgress;
using Xunit;

namespace Platform.Infrastructure.IntegrationTests.StageProgress;

/// <summary>The Executive Overview's progress figures on the stage model (2026-11-10).</summary>
public class StageOverviewTests
{
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly DateOnly Start = new(2026, 1, 20);
    private static readonly DateOnly AsOf = new(2026, 11, 10);
    private static readonly ProjectShape Shape = StageScheduleTests.Reference();

    private static Guid L(int n) => Shape.ActiveLevels.Single(l => l.LevelNumber == n).Id;

    private static ProjectStageData Data(IReadOnlyList<StageReportBlock>? blocks = null, IReadOnlyList<StageBaseline>? baselines = null, IReadOnlyList<PauseEvent>? pauses = null) =>
        new(new StageProject(ProjectId, "119", "نزل", "active", Start, 1, 5, Shape),
            DefaultStagePlan.Rows,
            (blocks ?? []).Select(b => new StageBlockRow(b.SubmissionId, ProjectId, b)).ToList(),
            baselines ?? [], [], pauses ?? []);

    /// <summary>Project 119's confirmed starting position (owner, 2026-11-10: day 4).</summary>
    private static IReadOnlyList<StageBaseline> Position119()
    {
        var list = DefaultStagePlan.Rows.Where(r => r.StageCode is "paperwork_1" or "paperwork_2" or "paperwork_3" or "excavation" or "foundation")
            .Select(r => new StageBaseline(r.StageCode, r.SubstepCode, null, null, 0, true)).ToList();
        list.AddRange(new[] { 0, 1, 2, 3 }.Select(n => new StageBaseline(StageCodes.Structure, null, L(n), null, 0, true)));
        list.AddRange(new[] { 0, 1 }.Select(n => new StageBaseline(StageCodes.Walls, null, L(n), null, 0, true)));
        list.Add(new StageBaseline(StageCodes.Structure, null, L(4), null, 4, false));
        list.Add(new StageBaseline(StageCodes.Walls, null, L(2), null, 4, false));
        return list;
    }

    private static OverviewReportRow Row(DateOnly date, string stage, bool met, int crew = 10, Guid? submission = null) =>
        new(ProjectId, date, stage, null, crew, "clear", false, null, false, null, met, submission ?? Guid.NewGuid());

    private static ExecutiveOverviewDto Calc(ProjectStageData data, IReadOnlyList<OverviewReportRow> rows, string? stage = null) =>
        ExecutiveOverviewCalculator.Calculate(
            [new OverviewProjectRow(ProjectId, "119", "نزل", "مكة", null, "active", Start, null)],
            new Dictionary<Guid, ProjectStageData> { [ProjectId] = data }, rows, [], AsOf, null, stage);

    [Fact]
    public void WorkComplete_Is158Of575_ForProject119sStartingPosition()
    {
        var dto = Calc(Data(baselines: Position119()), [Row(AsOf, StageCodes.Structure, true)]);
        dto.Projects.Single().WorkComplete.Should().BeApproximately(158d / 575d, 1e-9);
        dto.Tiles.WorkComplete.Should().BeApproximately(158d / 575d, 1e-9);
    }

    [Fact]
    public void TheStages_ComeFromThePlan_InOrder_WithBothNames()
    {
        var dto = Calc(Data(baselines: Position119()), [Row(AsOf, StageCodes.Structure, true)]);
        dto.Stages!.Select(s => s.Code).Should().StartWith(new[] { "paperwork_1", "paperwork_2", "paperwork_3", "excavation", "foundation", "structure", "walls" });
        dto.Stages.Should().NotContain(s => s.Code == "deconstruction", "119 needs no demolition");
        dto.Stages!.Single(s => s.Code == "foundation").NameEn.Should().Be("Foundation");
        var phases = dto.Projects.Single().Phases;
        phases.Single(p => p.Phase == "foundation").Complete.Should().Be(1);
        phases.Single(p => p.Phase == StageCodes.Structure).Complete.Should().BeApproximately(44d / 60d, 1e-9);
        phases.Single(p => p.Phase == StageCodes.Structure).NameAr.Should().Be("الهيكل الخرساني (الأعمدة والأسقف)");
    }

    [Fact]
    public void TheSchedule_IsJudgedAgainstTheMidpointPlan_With119WellBehind()
    {
        // 253 working days in, the plan (midpoint, +10%) expects far more than 27.5%.
        var project = Calc(Data(baselines: Position119()), [Row(AsOf, StageCodes.Structure, true)]).Projects.Single();
        project.ScheduleStatus.Should().Be("behind");
        project.ScheduleGapPoints.Should().BeLessThan(-5);
        project.TimeElapsed.Should().BeGreaterThan(0.4);
    }

    [Fact]
    public void ANewProject_OnPlan_IsOnTrack()
    {
        var start = AsOf.AddDays(-5); // Wed 5 Nov: 4 working days to today (Friday off)
        var blocks = new[] { 0, 1, 3, 4 }.Select(i => new StageReportBlock(Guid.NewGuid(), start.AddDays(i), "paperwork_1", "project_data", [], [], true, false)).ToList();
        var data = Data(blocks) with { Project = new StageProject(ProjectId, "N", "New", "active", start, 1, 5, Shape) };
        var project = ExecutiveOverviewCalculator.Calculate(
            [new OverviewProjectRow(ProjectId, "N", "New", null, null, "active", start, null)],
            new Dictionary<Guid, ProjectStageData> { [ProjectId] = data },
            blocks.Select(b => Row(b.Date, "paperwork_1", true)).ToList(), [], AsOf, null).Projects.Single();
        project.ScheduleStatus.Should().Be("on_track");
    }

    [Fact]
    public void CurrentStages_AreTheStagesReportedOnTheLastDay_InPlanOrder()
    {
        var sub = Guid.NewGuid();
        var dto = Calc(Data(baselines: Position119()),
            [Row(AsOf, StageCodes.Walls, true, submission: sub), Row(AsOf, StageCodes.Structure, true, submission: sub), Row(AsOf.AddDays(-1), "foundation", true)]);
        dto.Projects.Single().CurrentPhases.Should().Equal(StageCodes.Structure, StageCodes.Walls);
    }

    [Fact]
    public void AReportWithTwoBlocks_CountsItsCrewOnce()
    {
        var sub = Guid.NewGuid();
        var dto = Calc(Data(baselines: Position119()), [Row(AsOf, StageCodes.Structure, true, 12, sub), Row(AsOf, StageCodes.Walls, true, 12, sub)]);
        dto.Tiles.WorkersOnSite.Should().Be(12);
        dto.Projects.Single().ReportCount.Should().Be(1);
    }

    [Fact]
    public void TheStageFilter_ReadsThatStagesDays_AndItsPlannedFinishIncludesTheSlack()
    {
        var dto = Calc(Data(baselines: Position119()), [Row(AsOf, StageCodes.Structure, true)], StageCodes.Structure);
        dto.Tiles.StageEarnedDays.Should().Be(44);
        dto.Tiles.StagePlannedDays.Should().Be(60);
        // Structure ends on plan day 152 (92 + 60) in both schedules; +10% = 168 working days from 20 Jan.
        dto.Tiles.ForecastTarget.Should().Be(new StageDates(Start, new WorkingCalendar()).Finish(152));
    }

    [Fact]
    public void APause_ExcludesItsDaysFromElapsedTime_AndPushesTheTargetBack()
    {
        var pauses = new[] { new PauseEvent(new DateOnly(2026, 6, 1), PauseAction.Pause), new PauseEvent(new DateOnly(2026, 6, 15), PauseAction.Resume) };
        var plain = ProjectProgressCalculator.Compute(Data(baselines: Position119()), AsOf);
        var paused = ProjectProgressCalculator.Compute(Data(baselines: Position119(), pauses: pauses), AsOf);
        (plain.ElapsedWorkingDays - paused.ElapsedWorkingDays).Should().Be(12, "1-14 June holds 12 working days");
        paused.FinishTarget.Should().BeAfter(plain.FinishTarget!.Value);
    }

    [Fact]
    public void ReportsAfterTheAsOfDate_DoNotCount()
    {
        var later = new StageReportBlock(Guid.NewGuid(), AsOf.AddDays(1), "plastering_parking", null, [], [], true, false);
        ProjectProgressCalculator.Compute(Data([later], Position119()), AsOf).EarnedDays.Should().Be(158);
    }

    [Fact]
    public void AnOldReportWhosePhaseMatchesAStageCode_FeedsThePanels_ButNotThatStage()
    {
        var old = Row(AsOf.AddDays(-3), "foundation", false, 30) with { FromStageBlock = false };
        var dto = Calc(Data(baselines: Position119()), [Row(AsOf, StageCodes.Structure, true), old], "foundation");
        dto.Tiles.LateDays.Should().Be(0, "the old foundation report isn't a stage-block report");
        var all = Calc(Data(baselines: Position119()), [Row(AsOf, StageCodes.Structure, true), old]);
        all.Tiles.ReportsLast30Days.Should().Be(2, "it still counts as a report");
        all.Projects.Single().CurrentPhases.Should().Equal(StageCodes.Structure);
    }
}
