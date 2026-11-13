using System.Globalization;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Platform.Application.Common.Interfaces;
using Platform.Application.Reports;
using Platform.Domain.Notifications;

namespace Platform.Application.Notifications;

/// <summary>One scheduled report kind: on/off, Saudi time, and the days it goes out (none for monthly = last day).</summary>
public record ReportScheduleDto(bool Enabled, string Time, IReadOnlyList<DayOfWeek> Days);

/// <summary>
/// What the app sends on its own and when (owner, 2026-11-13) - the Messages page. The same record
/// is what the reminder and report services read every minute.
/// </summary>
public record MessagingScheduleDto(
    bool RemindersEnabled, IReadOnlyList<string> ReminderTimes, IReadOnlyList<DayOfWeek> ReminderDays,
    bool ReminderWhatsApp, bool ReminderTelegram,
    bool AssignedEnabled, bool AssignedWhatsApp, bool AssignedTelegram,
    ReportScheduleDto Daily, ReportScheduleDto Weekly, ReportScheduleDto Monthly,
    /// <summary>Read-only: which channels the server can actually send on.</summary>
    bool WhatsAppConfigured = false, bool TelegramConfigured = false)
{
    public bool RemindsOn(DateOnly day) => RemindersEnabled && ReminderDays.Contains(day.DayOfWeek);

    public bool ReminderChannel(string channel) => channel == AssignmentNotifier.WhatsAppChannel ? ReminderWhatsApp : ReminderTelegram;

    public bool AssignedChannel(string channel) => channel == AssignmentNotifier.WhatsAppChannel ? AssignedWhatsApp : AssignedTelegram;
}

public static class MessagingScheduleStore
{
    public static readonly DayOfWeek[] WorkingDays =
        [DayOfWeek.Saturday, DayOfWeek.Sunday, DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday];

    /// <summary>
    /// The saved schedule, or - until an administrator saves one - what the app did before the
    /// Messages page existed: reminders at Notifications:ReminderTimes on working days, every
    /// channel, and the reports at Reports:SendTime (daily Sat-Thu, weekly Thursday, monthly last day).
    /// </summary>
    public static async Task<MessagingScheduleDto> LoadAsync(
        IApplicationDbContext db, NotificationSettings notifications, string? reportSendTime, CancellationToken cancellationToken)
    {
        var row = await db.MessagingSchedules.AsNoTracking().OrderBy(s => s.CreatedAtUtc).FirstOrDefaultAsync(cancellationToken);
        return row is null ? Defaults(notifications, reportSendTime) : FromRow(row);
    }

    public static MessagingScheduleDto Defaults(NotificationSettings notifications, string? reportSendTime)
    {
        var time = IsTime(reportSendTime) ? reportSendTime! : "18:00";
        return new(
            true, SortTimes(notifications.EffectiveReminderTimes.Where(IsTime)), WorkingDays, true, true,
            true, true, true,
            new(true, time, WorkingDays), new(true, time, [DayOfWeek.Thursday]), new(true, time, []));
    }

    public static MessagingScheduleDto FromRow(MessagingSchedule s) => new(
        s.RemindersEnabled, s.ReminderTimes.Split(',', StringSplitOptions.RemoveEmptyEntries), Days(s.ReminderDays),
        s.ReminderWhatsApp, s.ReminderTelegram,
        s.AssignedEnabled, s.AssignedWhatsApp, s.AssignedTelegram,
        new(s.DailyReportEnabled, s.DailyReportTime, Days(s.DailyReportDays)),
        new(s.WeeklyReportEnabled, s.WeeklyReportTime, [s.WeeklyReportDay]),
        new(s.MonthlyReportEnabled, s.MonthlyReportTime, []));

    public static IReadOnlyList<DayOfWeek> Days(int mask) =>
        Enum.GetValues<DayOfWeek>().Where(d => MessagingSchedule.HasDay(mask, d)).OrderBy(d => ((int)d + 1) % 7).ToList();

    public static int Mask(IEnumerable<DayOfWeek> days) => days.Aggregate(0, (m, d) => m | (1 << (int)d));

    public static bool IsTime(string? value) =>
        value is not null && TimeOnly.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

    public static IReadOnlyList<string> SortTimes(IEnumerable<string> times) =>
        times.Select(t => t.Trim()).Distinct().OrderBy(t => t, StringComparer.Ordinal).ToList();
}

/// <summary>GET api/messaging-schedule (administrators).</summary>
public record GetMessagingScheduleQuery : IRequest<MessagingScheduleDto>;

public class GetMessagingScheduleQueryHandler(
    IApplicationDbContext db, NotificationSettings notifications, ReportSettings reports, WhatsAppSettings whatsApp, TelegramSettings telegram)
    : IRequestHandler<GetMessagingScheduleQuery, MessagingScheduleDto>
{
    public async Task<MessagingScheduleDto> Handle(GetMessagingScheduleQuery request, CancellationToken cancellationToken) =>
        (await MessagingScheduleStore.LoadAsync(db, notifications, reports.SendTime, cancellationToken))
        with { WhatsAppConfigured = whatsApp.IsConfigured, TelegramConfigured = telegram.IsConfigured };
}

/// <summary>PUT api/messaging-schedule (administrators): replaces the whole schedule.</summary>
public record SaveMessagingScheduleCommand(MessagingScheduleDto Schedule) : IRequest<MessagingScheduleDto>;

public class SaveMessagingScheduleCommandValidator : AbstractValidator<SaveMessagingScheduleCommand>
{
    public SaveMessagingScheduleCommandValidator()
    {
        RuleFor(x => x.Schedule).NotNull();
        RuleFor(x => x.Schedule.ReminderTimes).NotNull()
            .Must(t => t.Count <= 6).WithMessage("At most 6 reminder times a day.")
            .Must(t => t.All(MessagingScheduleStore.IsTime)).WithMessage("Reminder times must be HH:mm, e.g. 09:00.");
        RuleFor(x => x.Schedule.ReminderDays).NotNull();
        RuleFor(x => x.Schedule)
            .Must(s => !s.RemindersEnabled || (s.ReminderTimes.Count > 0 && s.ReminderDays.Count > 0))
            .WithMessage("Reminders need at least one time and one day - or switch them off.");
        foreach (var (name, pick) in new (string, Func<SaveMessagingScheduleCommand, ReportScheduleDto>)[]
                 { ("Daily", x => x.Schedule.Daily), ("Weekly", x => x.Schedule.Weekly), ("Monthly", x => x.Schedule.Monthly) })
        {
            RuleFor(x => pick(x)).NotNull().WithName(name)
                .Must(r => MessagingScheduleStore.IsTime(r.Time)).WithMessage($"The {name.ToLowerInvariant()} report time must be HH:mm, e.g. 18:00.");
        }
        RuleFor(x => x.Schedule.Daily).Must(r => !r.Enabled || r.Days.Count > 0)
            .WithMessage("The daily report needs at least one day - or switch it off.");
        RuleFor(x => x.Schedule.Weekly).Must(r => r.Days.Count == 1)
            .WithMessage("Pick the one day the weekly report goes out.");
    }
}

public class SaveMessagingScheduleCommandHandler(IApplicationDbContext db, ISender sender)
    : IRequestHandler<SaveMessagingScheduleCommand, MessagingScheduleDto>
{
    public async Task<MessagingScheduleDto> Handle(SaveMessagingScheduleCommand request, CancellationToken cancellationToken)
    {
        var s = request.Schedule;
        var row = await db.MessagingSchedules.OrderBy(x => x.CreatedAtUtc).FirstOrDefaultAsync(cancellationToken);
        if (row is null)
        {
            row = MessagingSchedule.Create();
            db.MessagingSchedules.Add(row);
        }
        row.Set(
            s.RemindersEnabled, string.Join(',', MessagingScheduleStore.SortTimes(s.ReminderTimes)), MessagingScheduleStore.Mask(s.ReminderDays),
            s.ReminderWhatsApp, s.ReminderTelegram,
            s.AssignedEnabled, s.AssignedWhatsApp, s.AssignedTelegram,
            s.Daily.Enabled, s.Daily.Time, MessagingScheduleStore.Mask(s.Daily.Days),
            s.Weekly.Enabled, s.Weekly.Time, s.Weekly.Days[0],
            s.Monthly.Enabled, s.Monthly.Time);
        await db.SaveChangesAsync(cancellationToken);
        return await sender.Send(new GetMessagingScheduleQuery(), cancellationToken);
    }
}
