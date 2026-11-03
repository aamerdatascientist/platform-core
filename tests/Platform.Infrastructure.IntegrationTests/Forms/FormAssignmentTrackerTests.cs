using FluentAssertions;
using Platform.Application.Common.Interfaces;
using Platform.Application.Forms.Assignments;
using Platform.Domain.Forms;
using Xunit;

namespace Platform.Infrastructure.IntegrationTests.Forms;

/// <summary>
/// Daily form assignments: which days are due, and how each is judged from the form's own
/// submissions - all pure, on hand-written rows.
/// </summary>
public class FormAssignmentTrackerTests
{
    // Tuesday 3 Nov 2026. Friday 30 Oct is the day off in the week before.
    private static readonly DateOnly Today = new(2026, 11, 3);
    private static readonly Guid Form = Guid.NewGuid();
    private static readonly Guid User = Guid.NewGuid();

    private static AssignmentInfo Assignment(DateOnly start, DateOnly? end = null) =>
        new(Guid.NewGuid(), User, "Qasim", Form, "Daily report", start, end, null, DateTime.UtcNow, "Admin");

    /// <summary>A submission at a Saudi local time on a Saudi day.</summary>
    private static (Guid, SubmissionStamp) Submitted(DateOnly day, int hour, int minute = 0) =>
        (Form, new SubmissionStamp(User, FormAssignmentTracker.SaudiDayStartUtc(day).AddHours(hour).AddMinutes(minute)));

    [Fact]
    public void Days_RunFromTheStartToToday_SkippingFriday_NewestFirst()
    {
        var days = FormAssignmentTracker.BuildDays(
            new[] { Assignment(new DateOnly(2026, 10, 29)) }, Array.Empty<(Guid, SubmissionStamp)>(),
            DateOnly.MinValue, Today, Today);

        // Thu 29, Sat 31, Sun 1, Mon 2, Tue 3 - no Friday 30.
        days.Select(d => d.Date.Day).Should().Equal(3, 2, 1, 31, 29);
    }

    [Fact]
    public void ADay_IsFilledByASubmissionOnThatSaudiDay_MissedWhenPast_PendingToday()
    {
        var stamps = new[]
        {
            Submitted(new DateOnly(2026, 11, 1), 9, 30),
            Submitted(new DateOnly(2026, 11, 1), 15),
            // 00:30 Saudi time on 2 Nov is still 1 Nov in UTC - it must count for 2 Nov.
            Submitted(new DateOnly(2026, 11, 2), 0, 30),
        };

        var days = FormAssignmentTracker.BuildDays(
            new[] { Assignment(new DateOnly(2026, 10, 31)) }, stamps, DateOnly.MinValue, Today, Today);

        var byDay = days.ToDictionary(d => d.Date.Day);
        byDay[31].Status.Should().Be(FormAssignmentTracker.Missed);
        byDay[1].Status.Should().Be(FormAssignmentTracker.Filled);
        byDay[1].SubmissionCount.Should().Be(2);
        byDay[1].FilledAtUtc.Should().Be(FormAssignmentTracker.SaudiDayStartUtc(new DateOnly(2026, 11, 1)).AddHours(9).AddMinutes(30));
        byDay[2].Status.Should().Be(FormAssignmentTracker.Filled);
        byDay[3].Status.Should().Be(FormAssignmentTracker.Pending);
    }

    [Fact]
    public void Days_StopAtTheEndDate_AndStayInsideTheRequestedRange()
    {
        var days = FormAssignmentTracker.BuildDays(
            new[] { Assignment(new DateOnly(2026, 10, 1), end: new DateOnly(2026, 11, 1)) },
            Array.Empty<(Guid, SubmissionStamp)>(), new DateOnly(2026, 10, 31), Today, Today);

        days.Select(d => d.Date.Day).Should().Equal(1, 31);
    }

    [Fact]
    public void Stop_KeepsTodayAndThePast_AndRemovesOneThatHasNotStarted()
    {
        var running = FormAssignment.Create(Form, User, new DateOnly(2026, 10, 1), null, null);
        running.Stop(Today);
        running.EndDate.Should().Be(Today);
        running.IsDeleted.Should().BeFalse();

        var future = FormAssignment.Create(Form, User, Today.AddDays(5), null, null);
        future.Stop(Today);
        future.IsDeleted.Should().BeTrue();
    }
}
