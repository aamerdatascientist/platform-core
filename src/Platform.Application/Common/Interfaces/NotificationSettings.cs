namespace Platform.Application.Common.Interfaces;

/// <summary>
/// The "Notifications" configuration section - what every channel (Telegram, WhatsApp) shares.
/// </summary>
public class NotificationSettings
{
    public static readonly IReadOnlyList<string> DefaultReminderTimes = new[] { "09:00", "14:00" };

    /// <summary>The site the links in messages point to, e.g. https://staging.asasksa.co.</summary>
    public string AppUrl { get; set; } = "";

    /// <summary>
    /// Saudi times ("HH:mm") on working days to remind anyone who hasn't filled today's form.
    /// Starts empty on purpose: the configuration binder appends to a pre-filled array instead
    /// of replacing it, so a default here would make Notifications__ReminderTimes__0 add a time
    /// rather than change one. Read <see cref="EffectiveReminderTimes"/>.
    /// </summary>
    public string[] ReminderTimes { get; set; } = Array.Empty<string>();

    public IReadOnlyList<string> EffectiveReminderTimes =>
        ReminderTimes.Any(t => !string.IsNullOrWhiteSpace(t)) ? ReminderTimes : DefaultReminderTimes;
}

/// <summary>What a channel answered for one message.</summary>
public record NotificationSendResult(bool Success, string? MessageId, string? Error);
