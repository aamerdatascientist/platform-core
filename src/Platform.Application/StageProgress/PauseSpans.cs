namespace Platform.Application.StageProgress;

public enum PauseAction { Pause, Resume }

/// <summary>One row of the <c>project-pause-log</c> form. <paramref name="Sequence"/> orders
/// entries made on the same date (a pause and a resume on one day) by when they were saved.</summary>
public sealed record PauseEvent(DateOnly Date, PauseAction Action, long Sequence = 0);

public static class PauseSpans
{
    /// <summary>
    /// The paused periods of a project: from a Pause date up to the day before the next Resume
    /// (work restarts on the resume date). A pause with no resume yet runs to
    /// <paramref name="asOf"/>. Repeated pauses or resumes out of order (the form refuses them,
    /// but old data could hold them) are ignored rather than double-counted.
    /// </summary>
    public static IReadOnlyList<DateSpan> From(IEnumerable<PauseEvent> events, DateOnly asOf)
    {
        var spans = new List<DateSpan>();
        DateOnly? pausedOn = null;
        foreach (var e in Ordered(events))
        {
            if (e.Action == PauseAction.Pause)
            {
                pausedOn ??= e.Date;
            }
            else if (pausedOn is { } p)
            {
                if (e.Date > p) spans.Add(new DateSpan(p, e.Date.AddDays(-1)));
                pausedOn = null;
            }
        }
        if (pausedOn is { } open && open <= asOf) spans.Add(new DateSpan(open, asOf));
        return spans;
    }

    /// <summary>Whether the project is paused right now (its last event is a Pause).</summary>
    public static bool IsPaused(IEnumerable<PauseEvent> events) =>
        Ordered(events).LastOrDefault()?.Action == PauseAction.Pause;

    private static IEnumerable<PauseEvent> Ordered(IEnumerable<PauseEvent> events) =>
        events.OrderBy(e => e.Date).ThenBy(e => e.Sequence);
}
