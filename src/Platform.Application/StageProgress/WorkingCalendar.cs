namespace Platform.Application.StageProgress;

/// <summary>An inclusive range of calendar days.</summary>
public readonly record struct DateSpan(DateOnly From, DateOnly To)
{
    public bool Contains(DateOnly day) => day >= From && day <= To;
}

/// <summary>
/// The one place stage-progress date arithmetic happens (owner, 2026-11-10). A working day is
/// any day that is not the weekly day off (default Friday, configurable) and not inside an
/// excluded span. Excluded spans are, today, the project's paused periods; the vacation
/// calendar (Eids, Ramadan) planned for later plugs in as more spans, with no change to any
/// calculation.
/// </summary>
public sealed class WorkingCalendar
{
    public DayOfWeek DayOff { get; }
    private readonly IReadOnlyList<DateSpan> _excluded;

    public WorkingCalendar(DayOfWeek dayOff = DayOfWeek.Friday, IEnumerable<DateSpan>? excluded = null)
    {
        DayOff = dayOff;
        _excluded = (excluded ?? []).ToList();
    }

    /// <summary>The same weekly day off with more spans excluded (e.g. a project's pauses).</summary>
    public WorkingCalendar Excluding(IEnumerable<DateSpan> spans) => new(DayOff, _excluded.Concat(spans));

    public bool IsWorkingDay(DateOnly day) => day.DayOfWeek != DayOff && !_excluded.Any(s => s.Contains(day));

    /// <summary>
    /// The date of the <paramref name="n"/>th working day counting from <paramref name="start"/>
    /// (day 1 is the first working day on or after start). n &lt;= 0 gives the last working day
    /// before start - "zero days in" has not started yet.
    /// </summary>
    public DateOnly WorkingDayNumber(DateOnly start, int n)
    {
        var day = start.AddDays(-1);
        if (n <= 0)
        {
            while (!IsWorkingDay(day)) day = day.AddDays(-1);
            return day;
        }
        while (n > 0)
        {
            day = day.AddDays(1);
            if (IsWorkingDay(day)) n--;
        }
        return day;
    }

    /// <summary>The date <paramref name="days"/> working days after <paramref name="from"/> (from itself not counted).</summary>
    public DateOnly AddWorkingDays(DateOnly from, int days) => days <= 0 ? from : WorkingDayNumber(from.AddDays(1), days);

    /// <summary>Working days from <paramref name="from"/> to <paramref name="to"/>, both included; 0 when to &lt; from.</summary>
    public int WorkingDaysBetween(DateOnly from, DateOnly to)
    {
        var count = 0;
        for (var d = from; d <= to; d = d.AddDays(1))
            if (IsWorkingDay(d)) count++;
        return count;
    }
}
