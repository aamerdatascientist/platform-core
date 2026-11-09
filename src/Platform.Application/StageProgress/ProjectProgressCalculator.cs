namespace Platform.Application.StageProgress;

/// <summary>
/// The working week every stage-progress date calculation uses: one weekly day off, Friday by
/// default (owner, 2026-11-10: "Friday only, for now; keep it configurable" - setting
/// <c>Schedule:WeeklyDayOff</c>, read once at startup). The form-assignment reminders, the
/// stakeholder reports and the overview all ask this one place.
/// </summary>
public static class WorkingWeek
{
    public static DayOfWeek DayOff { get; private set; } = DayOfWeek.Friday;

    public static void Configure(string? dayOff)
    {
        if (!string.IsNullOrWhiteSpace(dayOff) && Enum.TryParse<DayOfWeek>(dayOff.Trim(), ignoreCase: true, out var parsed))
            DayOff = parsed;
    }

    public static bool IsWorkingDay(DateOnly day) => day.DayOfWeek != DayOff;

    public static WorkingCalendar Calendar() => new(DayOff);
}

/// <summary>One project's progress as of a date: units, stages, schedule, dates.</summary>
public sealed record ProjectProgress(
    PlannedSchedule Schedule,
    IReadOnlyList<UnitProgress> Units,
    IReadOnlyList<StageProgressSummary> Stages,
    int ElapsedWorkingDays,
    /// <summary>Share of the planned unit-days the midpoint plan (with slack) expects by now; null without a start date.</summary>
    double? PlannedShare,
    DateOnly? FinishFastest,
    DateOnly? FinishSlowest,
    /// <summary>The planned finish progress is judged against: the midpoint of the range, with the 10% slack.</summary>
    DateOnly? FinishTarget,
    StageDates? Dates)
{
    public int EarnedDays => Units.Sum(u => u.EarnedDays);
    public int PlannedDays => Schedule.PlannedUnitDays;
    public double WorkComplete => PlannedDays == 0 ? 0 : (double)EarnedDays / PlannedDays;
    public bool Finished => PlannedDays > 0 && Units.All(u => u.Finished || u.EarnedDays >= u.Unit.PlannedDays);

    /// <summary>The planned (midpoint, slack) finish date of one stage; null without a start date.</summary>
    public DateOnly? StageTarget(string stageCode) =>
        Dates is null || Schedule.Stages.FirstOrDefault(s => s.StageCode == stageCode) is not { } stage
            ? null : Dates.Finish(stage.Window.Middle.End);
}

public static class ProjectProgressCalculator
{
    /// <summary>
    /// Progress as of <paramref name="asOf"/>: only reports, reopenings and pauses dated on or
    /// before it count (the stakeholder report's "at the start of the period" uses this).
    /// </summary>
    public static ProjectProgress Compute(ProjectStageData data, DateOnly asOf)
    {
        var schedule = StageScheduleBuilder.Build(data.Plan, data.Project.Shape);
        var blocks = data.Blocks.Where(b => b.Block.Date <= asOf).Select(b => b.Block).ToList();
        var units = StageProgressCalculator.Compute(schedule.Units, blocks, data.Baselines, data.Reopenings.Where(r => r.On <= asOf));
        var stages = StageProgressCalculator.ByStage(schedule, units);

        if (data.Project.StartDate is not { } start)
            return new ProjectProgress(schedule, units, stages, 0, null, null, null, null, null);

        var pauses = PauseSpans.From(data.Pauses.Where(p => p.Date <= asOf), asOf);
        var dates = new StageDates(start, WorkingWeek.Calendar().Excluding(pauses));
        var elapsed = asOf < start ? 0 : dates.Elapsed(asOf);
        return new ProjectProgress(
            schedule, units, stages, elapsed,
            StageProgressCalculator.PlannedShare(schedule, elapsed, StageDates.Slack),
            dates.Finish(schedule.Project.Fastest.End),
            dates.Finish(schedule.Project.Slowest.End),
            dates.Finish(schedule.Project.Middle.End),
            dates);
    }
}
