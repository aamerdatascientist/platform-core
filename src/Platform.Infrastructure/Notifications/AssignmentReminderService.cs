using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Platform.Application.Common.Interfaces;
using Platform.Application.Forms.Assignments;
using Platform.Application.Notifications;

namespace Platform.Infrastructure.Notifications;

/// <summary>
/// Once a minute: on a working day, once a reminder time (Notifications:ReminderTimes, Saudi time)
/// has passed, reminds everyone who hasn't filled today's assigned form. Only the latest time
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

    /// <summary>The 1-based reminder slot due at <paramref name="saudiNow"/>, or null.</summary>
    public static int? DueSlot(IReadOnlyList<string> reminderTimes, TimeOnly saudiNow)
    {
        int? due = null;
        for (var i = 0; i < reminderTimes.Count; i++)
        {
            if (!TimeOnly.TryParseExact(reminderTimes[i], "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var at)) continue;
            if (saudiNow >= at && saudiNow.ToTimeSpan() - at.ToTimeSpan() < LateWindow) due = i + 1;
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
                if (FormAssignmentTracker.IsWorkingDay(today) && DueSlot(_settings.EffectiveReminderTimes, TimeOnly.FromDateTime(saudiNow)) is { } slot)
                {
                    using var scope = _scopeFactory.CreateScope();
                    var notifier = scope.ServiceProvider.GetRequiredService<AssignmentNotifier>();
                    var sent = await notifier.SendDueRemindersAsync(today, slot, stoppingToken);
                    if (sent > 0) _logger.LogInformation("Sent {Count} assignment reminders (slot {Slot}).", sent, slot);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Assignment reminders failed; trying again next minute.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
