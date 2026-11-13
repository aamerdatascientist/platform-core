using Platform.Domain.Common;

namespace Platform.Domain.Notifications;

/// <summary>
/// What the app sends on its own and when (owner, 2026-11-13: "full control of what goes where"),
/// set by administrators on the Messages page. One row; until it exists the server settings
/// (Notifications:ReminderTimes, Reports:SendTime) and the original schedule apply.
/// Days are a bit mask over <see cref="DayOfWeek"/> (bit 0 = Sunday ... bit 6 = Saturday).
/// Times are Saudi "HH:mm".
/// </summary>
public class MessagingSchedule : AuditableEntity
{
    /// <summary>Saturday to Thursday - the working week.</summary>
    public const int WorkingDays = 0b1011111; // every day but Friday (bit 5)

    public bool RemindersEnabled { get; private set; }
    /// <summary>Comma-separated, sorted, e.g. "09:00,14:00".</summary>
    public string ReminderTimes { get; private set; } = default!;
    public int ReminderDays { get; private set; }
    public bool ReminderWhatsApp { get; private set; }
    public bool ReminderTelegram { get; private set; }

    /// <summary>The "you've been assigned a form" message, sent when an assignment is made.</summary>
    public bool AssignedEnabled { get; private set; }
    public bool AssignedWhatsApp { get; private set; }
    public bool AssignedTelegram { get; private set; }

    public bool DailyReportEnabled { get; private set; }
    public string DailyReportTime { get; private set; } = default!;
    public int DailyReportDays { get; private set; }

    public bool WeeklyReportEnabled { get; private set; }
    public string WeeklyReportTime { get; private set; } = default!;
    public DayOfWeek WeeklyReportDay { get; private set; }

    /// <summary>Sent on the last day of each month.</summary>
    public bool MonthlyReportEnabled { get; private set; }
    public string MonthlyReportTime { get; private set; } = default!;

    private MessagingSchedule() { }

    public static MessagingSchedule Create() => new();

    public void Set(
        bool remindersEnabled, string reminderTimes, int reminderDays, bool reminderWhatsApp, bool reminderTelegram,
        bool assignedEnabled, bool assignedWhatsApp, bool assignedTelegram,
        bool dailyEnabled, string dailyTime, int dailyDays,
        bool weeklyEnabled, string weeklyTime, DayOfWeek weeklyDay,
        bool monthlyEnabled, string monthlyTime)
    {
        RemindersEnabled = remindersEnabled;
        ReminderTimes = reminderTimes;
        ReminderDays = reminderDays;
        ReminderWhatsApp = reminderWhatsApp;
        ReminderTelegram = reminderTelegram;
        AssignedEnabled = assignedEnabled;
        AssignedWhatsApp = assignedWhatsApp;
        AssignedTelegram = assignedTelegram;
        DailyReportEnabled = dailyEnabled;
        DailyReportTime = dailyTime;
        DailyReportDays = dailyDays;
        WeeklyReportEnabled = weeklyEnabled;
        WeeklyReportTime = weeklyTime;
        WeeklyReportDay = weeklyDay;
        MonthlyReportEnabled = monthlyEnabled;
        MonthlyReportTime = monthlyTime;
    }

    public static bool HasDay(int mask, DayOfWeek day) => (mask & (1 << (int)day)) != 0;
}
