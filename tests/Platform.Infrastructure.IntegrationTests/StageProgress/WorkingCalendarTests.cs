using FluentAssertions;
using Platform.Application.StageProgress;
using Xunit;

namespace Platform.Infrastructure.IntegrationTests.StageProgress;

public class WorkingCalendarTests
{
    private static readonly DateOnly Start119 = new(2026, 1, 20);

    [Fact]
    public void Fridays_AreNotWorkingDays_ByDefault()
    {
        var calendar = new WorkingCalendar();
        calendar.IsWorkingDay(new DateOnly(2026, 11, 13)).Should().BeFalse(); // Friday
        calendar.IsWorkingDay(new DateOnly(2026, 11, 14)).Should().BeTrue();  // Saturday
    }

    [Fact]
    public void TheDayOff_IsConfigurable()
    {
        var calendar = new WorkingCalendar(DayOfWeek.Saturday);
        calendar.IsWorkingDay(new DateOnly(2026, 11, 13)).Should().BeTrue();
        calendar.IsWorkingDay(new DateOnly(2026, 11, 14)).Should().BeFalse();
    }

    [Fact]
    public void Project119_PlanDay136_FallsOn27June_AndTodayIsWorkingDay253()
    {
        var dates = new StageDates(Start119, new WorkingCalendar());
        dates.Finish(136, withSlack: false).Should().Be(new DateOnly(2026, 6, 27));
        dates.Finish(328, withSlack: false).Should().Be(new DateOnly(2027, 2, 6));
        dates.Finish(328).Should().Be(new DateOnly(2027, 3, 16), "361 working days with the slack");
        dates.Elapsed(new DateOnly(2026, 11, 10)).Should().Be(253);
    }

    [Fact]
    public void PausedDays_AreExcludedFromElapsedTime_AndPushPlannedDatesForward()
    {
        var events = new[]
        {
            new PauseEvent(new DateOnly(2026, 3, 1), PauseAction.Pause),   // Sunday
            new PauseEvent(new DateOnly(2026, 3, 8), PauseAction.Resume),  // the next Sunday: 6 working days paused
        };
        var spans = PauseSpans.From(events, new DateOnly(2026, 11, 10));
        spans.Should().Equal(new DateSpan(new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 7)));

        var plain = new StageDates(Start119, new WorkingCalendar());
        var paused = new StageDates(Start119, new WorkingCalendar().Excluding(spans));
        paused.Elapsed(new DateOnly(2026, 11, 10)).Should().Be(253 - 6);
        paused.Finish(136, withSlack: false).Should().Be(new WorkingCalendar().AddWorkingDays(plain.Finish(136, withSlack: false), 6));
    }

    [Fact]
    public void AnOpenPause_RunsToToday_AndMarksTheProjectPaused()
    {
        var events = new[] { new PauseEvent(new DateOnly(2026, 11, 1), PauseAction.Pause) };
        PauseSpans.From(events, new DateOnly(2026, 11, 10)).Should().Equal(new DateSpan(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 10)));
        PauseSpans.IsPaused(events).Should().BeTrue();
        PauseSpans.IsPaused([.. events, new PauseEvent(new DateOnly(2026, 11, 9), PauseAction.Resume)]).Should().BeFalse();
    }
}
