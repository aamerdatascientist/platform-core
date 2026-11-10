namespace Platform.Application.StageProgress;

/// <summary>
/// One stage block of a daily report (or a whole v2 report with one block). Levels are the
/// floors ticked (or the one level for structure); sides are 1..N; a fixed stage has neither.
/// </summary>
/// <remarks><paramref name="SavedAtUtc"/>: when the block was saved - a reopening voids the
/// "finished" of blocks saved before it, whatever their report date.</remarks>
public sealed record StageReportBlock(
    Guid SubmissionId, DateOnly Date, string StageCode, string? SubstepCode,
    IReadOnlyList<Guid> LevelIds, IReadOnlyList<int> Sides, bool PlanMet, bool Finished, DateTime? SavedAtUtc = null);

/// <summary>
/// A unit's position recorded when a project joins the stage model part-way through (owner,
/// 2026-11-10: project 119). It has no date: a finished baseline unit counts as finished but
/// takes no part in per-unit lateness, and an in-progress one carries its earned days.
/// </summary>
public sealed record StageBaseline(string StageCode, string? SubstepCode, Guid? LevelId, int? Side, int EarnedDays, bool Finished)
{
    public string Key => UnitKeys.For(StageCode, SubstepCode, LevelId, Side);
}

/// <summary>An administrator reopened a finished unit at <paramref name="AtUtc"/> (on <paramref name="On"/>):
/// every "finished" saved before then no longer counts, and the unit can be reported again. Without
/// a time, the date decides (finishes dated on or before it are void).</summary>
public sealed record StageReopening(string UnitKey, DateOnly On, DateTime? AtUtc = null);

public sealed record UnitProgress(
    StageUnit Unit, int EarnedDays, bool Finished, DateOnly? ActualStart, DateOnly? ActualFinish, bool FromBaseline)
{
    public double Complete => Unit.PlannedDays == 0 ? 0 : (double)EarnedDays / Unit.PlannedDays;
    public bool Started => EarnedDays > 0 || Finished || ActualStart is not null;
}

public sealed record StageProgressSummary(string StageCode, string NameEn, string NameAr, int EarnedDays, int PlannedDays)
{
    public double Complete => PlannedDays == 0 ? 0 : (double)EarnedDays / PlannedDays;
}

/// <summary>
/// Days earned. Each block whose day's plan was met earns one day for every unit it names
/// (each floor ticked, each side ticked, or the stage's single unit), at most one day per
/// unit per date (owner, 2026-11-10: one combined report and separate reports give the same
/// result), capped at the unit's planned days. A "finished" block sets the unit to its planned
/// days and dates its finish. Earned days are recomputed from the reports every time, so
/// changing a plan's days re-caps them - raising the days later gives back what was earned.
/// </summary>
public static class StageProgressCalculator
{
    public static IReadOnlyList<UnitProgress> Compute(
        IReadOnlyList<StageUnit> units,
        IEnumerable<StageReportBlock> blocks,
        IEnumerable<StageBaseline>? baselines = null,
        IEnumerable<StageReopening>? reopenings = null)
    {
        var byKey = new Dictionary<string, List<(DateOnly Date, bool PlanMet, bool Finished, DateTime? SavedAt)>>();
        foreach (var block in blocks)
        {
            foreach (var key in KeysOf(block))
            {
                if (!byKey.TryGetValue(key, out var list)) byKey[key] = list = [];
                list.Add((block.Date, block.PlanMet, block.Finished, block.SavedAtUtc));
            }
        }
        var baselineByKey = (baselines ?? []).GroupBy(b => b.Key).ToDictionary(g => g.Key, g => g.Last());
        var lastReopen = (reopenings ?? []).GroupBy(r => r.UnitKey)
            .ToDictionary(g => g.Key, g => g.OrderBy(r => r.AtUtc ?? DateTime.MinValue).ThenBy(r => r.On).Last());

        return units.Select(unit =>
        {
            var reports = byKey.GetValueOrDefault(unit.Key) ?? [];
            var baseline = baselineByKey.GetValueOrDefault(unit.Key);
            var reopen = lastReopen.GetValueOrDefault(unit.Key);
            bool AfterReopen((DateOnly Date, bool PlanMet, bool Finished, DateTime? SavedAt) r) =>
                reopen is null || (r.SavedAt is { } saved && reopen.AtUtc is { } at ? saved > at : r.Date > reopen.On);

            var metDays = reports.Where(r => r.PlanMet).Select(r => r.Date).Distinct().Count();
            var earned = Math.Min(unit.PlannedDays, Math.Max(0, (baseline?.EarnedDays ?? 0) + metDays));

            var finishes = reports.Where(r => r.Finished && AfterReopen(r)).Select(r => r.Date).ToList();
            var baselineFinished = baseline?.Finished == true && reopen is null;
            var finished = finishes.Count > 0 || baselineFinished;
            DateOnly? finishDate = finishes.Count > 0 ? finishes.Min() : null;
            if (finished) earned = unit.PlannedDays;

            DateOnly? start = reports.Count > 0 ? reports.Min(r => r.Date) : null;
            // A unit already under way at the baseline started before any report we have.
            if (baseline is not null && (baseline.EarnedDays > 0 || baseline.Finished)) start = null;

            return new UnitProgress(unit, earned, finished, start, finishDate, baseline is not null);
        }).ToList();
    }

    /// <summary>The unit keys a block reports on.</summary>
    public static IEnumerable<string> KeysOf(StageReportBlock block)
    {
        if (block.LevelIds.Count > 0)
            return block.LevelIds.Distinct().Select(l => UnitKeys.For(block.StageCode, block.SubstepCode, l, null));
        if (block.Sides.Count > 0)
            return block.Sides.Distinct().Select(s => UnitKeys.For(block.StageCode, block.SubstepCode, null, s));
        return [UnitKeys.For(block.StageCode, block.SubstepCode, null, null)];
    }

    public static IReadOnlyList<StageProgressSummary> ByStage(PlannedSchedule schedule, IReadOnlyList<UnitProgress> units) =>
        schedule.Stages.Select(s =>
        {
            var inStage = units.Where(u => u.Unit.StageCode == s.StageCode).ToList();
            return new StageProgressSummary(s.StageCode, s.NameEn, s.NameAr, inStage.Sum(u => u.EarnedDays), inStage.Sum(u => u.Unit.PlannedDays));
        }).ToList();

    /// <summary>
    /// The share of all planned unit-days the plan expects to be earned after
    /// <paramref name="elapsedWorkingDays"/>, on the midpoint schedule (owner, 2026-11-10:
    /// compare against the average of the fastest and slowest dates), stretched by the slack.
    /// Each unit is expected to earn its days evenly across its planned window.
    /// </summary>
    public static double PlannedShare(PlannedSchedule schedule, int elapsedWorkingDays, double slack)
    {
        var total = schedule.PlannedUnitDays;
        if (total == 0) return 0;
        var t = elapsedWorkingDays / (1 + slack);
        double expected = 0;
        foreach (var unit in schedule.Units)
        {
            if (!schedule.UnitWindows.TryGetValue(unit.Key, out var window)) continue;
            var mid = window.Middle;
            var length = Math.Max(1, mid.End - mid.Start);
            expected += unit.PlannedDays * Math.Clamp((t - mid.Start) / length, 0, 1);
        }
        return expected / total;
    }
}

/// <summary>Planned working-day offsets as dates, for one project.</summary>
public sealed class StageDates
{
    /// <summary>Slack added on planned dates (owner, 2026-11-03 and kept 2026-11-10): +10%.</summary>
    public const double Slack = 0.10;

    private readonly DateOnly _start;
    private readonly WorkingCalendar _calendar;

    /// <param name="start">The project's planned start date (working day 1 is the first working day on or after it).</param>
    /// <param name="calendar">The project's calendar: the weekly day off plus its paused periods.</param>
    public StageDates(DateOnly start, WorkingCalendar calendar)
    {
        _start = start;
        _calendar = calendar;
    }

    /// <summary>Days scaled by the slack, rounded up - the way the overview's target always worked.</summary>
    public static int WithSlack(int days) => (int)Math.Ceiling(days * (1 + Slack) - 1e-9);

    /// <summary>The date a planned span ending after <paramref name="days"/> working days finishes.</summary>
    public DateOnly Finish(int days, bool withSlack = true) =>
        _calendar.WorkingDayNumber(_start, withSlack ? WithSlack(days) : days);

    /// <summary>Working days from the start to <paramref name="asOf"/>, both included, paused days excluded.</summary>
    public int Elapsed(DateOnly asOf) => _calendar.WorkingDaysBetween(_start, asOf);

    public WorkingCalendar Calendar => _calendar;
}
