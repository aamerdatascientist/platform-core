using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Platform.Application.Common.Interfaces;
using Platform.Application.Forms.Assignments;
using Platform.Application.Notifications;

namespace Platform.Infrastructure.Notifications;

/// <summary>
/// Once a minute: on a reminder day, once a reminder time (the Messages page; until it is saved,
/// Notifications:ReminderTimes - Saudi time) has passed, reminds everyone who hasn't filled today's assigned form. Only the latest time
/// that has passed is sent, and only within <see cref="LateWindow"/> of it - an API that comes
/// back from a restart in the afternoon sends the afternoon reminder, not the morning one as
/// well. Sending twice is impossible either way (see NotificationLog).
/// </summary>
public class AssignmentReminderService : BackgroundService
{
    private static readonly TimeSpan LateWindow = TimeSpan.FromHours(2);
    private static readonly TimeSpan SaudiArabiaOffset = TimeSpan.FromHours(3);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly NotificationSettings _settings;
    private readonly TelegramSettings _telegram;
    private readonly WhatsAppSettings _whatsApp;
    private readonly ILogger<AssignmentReminderService> _logger;

    public AssignmentReminderService(
        IServiceScopeFactory scopeFactory, NotificationSettings settings, TelegramSettings telegram,
        WhatsAppSettings whatsApp, ILogger<AssignmentReminderService> logger)
    {
        _scopeFactory = scopeFactory;
        _settings = settings;
        _telegram = telegram;
        _whatsApp = whatsApp;
        _logger = logger;
    }

    /// <summary>The latest reminder time ("HH:mm") due at <paramref name="saudiNow"/>, or null.</summary>
    public static string? DueTime(IReadOnlyList<string> reminderTimes, TimeOnly saudiNow)
    {
        string? due = null;
        TimeOnly latest = default;
        foreach (var time in reminderTimes)
        {
            if (!TimeOnly.TryParseExact(time, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var at)) continue;
            if (saudiNow >= at && saudiNow.ToTimeSpan() - at.ToTimeSpan() < LateWindow && (due is null || at > latest)) (due, latest) = (time, at);
        }
        return due;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_telegram.IsConfigured && !_whatsApp.IsConfigured)
        {
            _logger.LogInformation("No notification channel (Telegram, WhatsApp) is configured - assignment reminders are off.");
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        do
        {
            try
            {
                var saudiNow = DateTime.UtcNow + SaudiArabiaOffset;
                var today = DateOnly.FromDateTime(saudiNow);
                if (FormAssignmentTracker.IsWorkingDay(today))
                {
                    using var scope = _scopeFactory.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
                    var schedule = await MessagingScheduleStore.LoadAsync(db, _settings, null, stoppingToken);
                    if (schedule.RemindsOn(today) && DueTime(schedule.ReminderTimes, TimeOnly.FromDateTime(saudiNow)) is { } time)
                    {
                        var notifier = scope.ServiceProvider.GetRequiredService<AssignmentNotifier>();
                        var sent = await notifier.SendDueRemindersAsync(today, time, schedule, stoppingToken);
                        if (sent > 0) _logger.LogInformation("Sent {Count} assignment reminders ({Time}).", sent, time);
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Assignment reminders failed; trying again next minute.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
