using FluentAssertions;
using Platform.Application.Common.Interfaces;
using Platform.Application.Notifications;
using Platform.Application.Reports;
using Platform.Domain.Notifications;
using Platform.Domain.Reports;
using Platform.Infrastructure.Reports;
using Xunit;

namespace Platform.Infrastructure.IntegrationTests.Forms;

/// <summary>The Messages page's schedule (owner, 2026-11-13): reminders and reports follow it.</summary>
public class MessagingScheduleTests
{
    private static readonly MessagingScheduleDto Defaults = MessagingScheduleStore.Defaults(new NotificationSettings(), "18:00");

    [Fact]
    public void WithoutASavedSchedule_TheOldBehaviourApplies()
    {
        Defaults.ReminderTimes.Should().Equal("09:00", "14:00");
        Defaults.ReminderDays.Should().NotContain(DayOfWeek.Friday).And.HaveCount(6);
        Defaults.Weekly.Days.Should().Equal(DayOfWeek.Thursday);
        Defaults.Daily.Time.Should().Be("18:00");
    }

    [Fact]
    public void ASavedRow_RoundTrips()
    {
        var row = MessagingSchedule.Create();
        row.Set(true, "07:30,16:00", MessagingScheduleStore.Mask([DayOfWeek.Sunday, DayOfWeek.Monday]), true, false,
            false, true, true,
            true, "20:00", MessagingScheduleStore.Mask([DayOfWeek.Saturday]), true, "19:00", DayOfWeek.Sunday, false, "17:00");
        var dto = MessagingScheduleStore.FromRow(row);
        dto.ReminderTimes.Should().Equal("07:30", "16:00");
        dto.ReminderDays.Should().Equal(DayOfWeek.Sunday, DayOfWeek.Monday);
        dto.ReminderChannel(AssignmentNotifier.TelegramChannel).Should().BeFalse();
        dto.AssignedEnabled.Should().BeFalse();
        dto.Weekly.Should().BeEquivalentTo(new ReportScheduleDto(true, "19:00", [DayOfWeek.Sunday]));
        dto.Monthly.Enabled.Should().BeFalse();
    }

    [Fact]
    public void DaysAreListedFromSaturday() =>
        MessagingScheduleStore.Days(MessagingSchedule.WorkingDays).Should().Equal(MessagingScheduleStore.WorkingDays);

    [Fact]
    public void Reports_GoOutOnTheirOwnDayAndTime()
    {
        var s = Defaults with
        {
            Daily = new(false, "18:00", MessagingScheduleStore.WorkingDays),
            Weekly = new(true, "08:00", [DayOfWeek.Sunday]),
            Monthly = new(true, "12:00", []),
        };
        var sunday = new DateOnly(2026, 11, 29);
        StakeholderReportScheduler.DueKinds(s, sunday).Should().Equal(ReportRun.Weekly);
        StakeholderReportScheduler.DueNow(s, sunday, new TimeOnly(7, 59)).Should().BeEmpty();
        StakeholderReportScheduler.DueNow(s, sunday, new TimeOnly(8, 0)).Should().Equal(ReportRun.Weekly);
        var monthEnd = new DateOnly(2026, 11, 30);
        StakeholderReportScheduler.DueNow(s, monthEnd, new TimeOnly(12, 30)).Should().Equal(ReportRun.Monthly);
        StakeholderReportScheduler.DueNow(s, monthEnd, new TimeOnly(18, 30)).Should().BeEmpty("the daily report is switched off");
    }

    [Fact]
    public void AWeeklyReportOnASunday_CoversTheSevenDaysToIt()
    {
        var sunday = new DateOnly(2026, 11, 29);
        StakeholderReportBuilder.Period(ReportRun.Weekly, sunday).From.Should().Be(new DateOnly(2026, 11, 23));
        var thursday = new DateOnly(2026, 11, 26);
        StakeholderReportBuilder.Period(ReportRun.Weekly, thursday).From.Should().Be(new DateOnly(2026, 11, 21), "Saturday - the working week, as before");
    }

    [Fact]
    public async Task RemindersSwitchedOff_OrNotOnThatDay_SendNothing()
    {
        var w = await ManualRemindersTests.CreateAsync(); // Saturday
        (await w.Notifier.SendDueRemindersAsync(w.Today, "09:00", Defaults with { RemindersEnabled = false }, default)).Should().Be(0);
        (await w.Notifier.SendDueRemindersAsync(w.Today, "09:00", Defaults with { ReminderDays = [DayOfWeek.Sunday] }, default)).Should().Be(0);
        (await w.Notifier.SendDueRemindersAsync(w.Today, "09:00", Defaults with { ReminderTelegram = false }, default)).Should().Be(0, "Telegram is the only linked channel");
        w.Telegram.Sent.Should().BeEmpty();
        (await w.Notifier.SendDueRemindersAsync(w.Today, "09:00", Defaults, default)).Should().Be(1);
    }

    [Fact]
    public async Task ChangingTheTimes_NeverResendsTheSameTime()
    {
        var w = await ManualRemindersTests.CreateAsync();
        (await w.Notifier.SendDueRemindersAsync(w.Today, "09:00", Defaults, default)).Should().Be(1);
        var edited = Defaults with { ReminderTimes = ["07:00", "09:00", "14:00"] };
        (await w.Notifier.SendDueRemindersAsync(w.Today, "09:00", edited, default)).Should().Be(0);
        (await w.Notifier.SendDueRemindersAsync(w.Today, "07:00", edited, default)).Should().Be(1);
    }

    [Fact]
    public void TheValidator_RefusesBadTimesAndEmptyDays()
    {
        var v = new SaveMessagingScheduleCommandValidator();
        v.Validate(new SaveMessagingScheduleCommand(Defaults)).IsValid.Should().BeTrue();
        v.Validate(new SaveMessagingScheduleCommand(Defaults with { ReminderTimes = ["9am"] })).IsValid.Should().BeFalse();
        v.Validate(new SaveMessagingScheduleCommand(Defaults with { ReminderDays = [] })).IsValid.Should().BeFalse();
        v.Validate(new SaveMessagingScheduleCommand(Defaults with { RemindersEnabled = false, ReminderDays = [] })).IsValid.Should().BeTrue();
        v.Validate(new SaveMessagingScheduleCommand(Defaults with { Weekly = new(true, "18:00", []) })).IsValid.Should().BeFalse();
        v.Validate(new SaveMessagingScheduleCommand(Defaults with { Monthly = new(true, "25:00", []) })).IsValid.Should().BeFalse();
    }
}
