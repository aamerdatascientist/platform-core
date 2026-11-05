using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Platform.Application.Common.Interfaces;
using Platform.Application.Forms.Assignments;
using Platform.Domain.Notifications;

namespace Platform.Application.Notifications;

/// <summary>
/// Messages about form assignments: one when a user is assigned, and reminders on working days
/// to anyone who hasn't filled today's form yet. Each goes out on every channel that is
/// configured on the server AND linked for the user - Telegram (chat linked through the bot)
/// and/or WhatsApp (mobile number). Every message is logged in NotificationLogs, and a failure
/// is logged there too - sending never breaks the request or the scheduler that triggered it.
/// </summary>
public class AssignmentNotifier
{
    public const string WhatsAppChannel = "whatsapp";
    public const string TelegramChannel = "telegram";
    public const string AssignedKind = "assigned";
    public const string TestKind = "test";
    public static string ReminderKind(int slot) => $"reminder-{slot}";

    private static readonly string[] ArabicMonths =
        { "يناير", "فبراير", "مارس", "أبريل", "مايو", "يونيو", "يوليو", "أغسطس", "سبتمبر", "أكتوبر", "نوفمبر", "ديسمبر" };

    private readonly IApplicationDbContext _db;
    private readonly IDynamicDataRepository _dynamicData;
    private readonly IWhatsAppSender _whatsApp;
    private readonly ITelegramClient _telegram;
    private readonly WhatsAppSettings _whatsAppSettings;
    private readonly TelegramSettings _telegramSettings;
    private readonly NotificationSettings _settings;
    private readonly ILogger<AssignmentNotifier> _logger;

    public AssignmentNotifier(
        IApplicationDbContext db, IDynamicDataRepository dynamicData,
        IWhatsAppSender whatsApp, ITelegramClient telegram,
        WhatsAppSettings whatsAppSettings, TelegramSettings telegramSettings, NotificationSettings settings,
        ILogger<AssignmentNotifier> logger)
    {
        _db = db;
        _dynamicData = dynamicData;
        _whatsApp = whatsApp;
        _telegram = telegram;
        _whatsAppSettings = whatsAppSettings;
        _telegramSettings = telegramSettings;
        _settings = settings;
        _logger = logger;
    }

    /// <summary>True when at least one channel can send.</summary>
    public bool AnyChannelConfigured => _whatsAppSettings.IsConfigured || _telegramSettings.IsConfigured;

    /// <summary>"3 نوفمبر 2026" - the Gregorian date in Arabic (ar-SA's own calendar is Hijri).</summary>
    public static string ArabicDate(DateOnly date) => $"{date.Day} {ArabicMonths[date.Month - 1]} {date.Year}";

    /// <summary>
    /// One message, in the shapes each channel needs: WhatsApp sends an approved template with
    /// (name, form, date, link); Telegram sends <see cref="Text"/> with a button to <see cref="Link"/>.
    /// </summary>
    public record Message(string Template, string Name, string Form, string Date, string Link, string Text, string ButtonText)
    {
        public IReadOnlyList<string> TemplateParameters => new[] { Name, Form, Date, Link };
    }

    public static Message AssignedMessage(string template, string name, string form, DateOnly start, string link) => new(
        template, name, form, ArabicDate(start), link,
        $"مرحباً {name}، تم تكليفك بتعبئة نموذج «{form}» في كل يوم عمل ابتداءً من {ArabicDate(start)}.",
        "مهامي");

    public static Message ReminderMessage(string template, string name, string form, DateOnly day, string link) => new(
        template, name, form, ArabicDate(day), link,
        $"مرحباً {name}، تذكير بأن نموذج «{form}» لهذا اليوم ({ArabicDate(day)}) لم تتم تعبئته بعد.",
        "تعبئة النموذج");

    public static Message TestMessage(string template, string name, DateOnly day, string link) => new(
        template, name, "اختبار", ArabicDate(day), link,
        $"مرحباً {name}، هذه رسالة تجريبية من أساس. ستصلك هنا تذكيرات النماذج المكلّف بها.",
        "مهامي");

    /// <summary>Tells each newly assigned user. Called right after the assignments are saved.</summary>
    public async Task NotifyAssignedAsync(IReadOnlyCollection<Guid> assignmentIds, CancellationToken cancellationToken)
    {
        if (!AnyChannelConfigured || assignmentIds.Count == 0) return;

        var rows = await _db.FormAssignments
            .Where(a => assignmentIds.Contains(a.Id))
            .Join(_db.Users, a => a.UserId, u => u.Id,
                (a, u) => new { a.Id, a.UserId, a.FormDefinitionId, a.StartDate, u.DisplayName, u.PhoneNumber, u.TelegramChatId })
            .Join(_db.FormDefinitions, x => x.FormDefinitionId, f => f.Id,
                (x, f) => new { x.Id, x.UserId, x.StartDate, x.DisplayName, x.PhoneNumber, x.TelegramChatId, FormName = f.Name })
            .ToListAsync(cancellationToken);

        foreach (var row in rows)
        {
            var message = AssignedMessage(_whatsAppSettings.AssignedTemplate, row.DisplayName, row.FormName, row.StartDate, Link("/assignments"));
            foreach (var (channel, recipient) in ChannelsFor(row.PhoneNumber, row.TelegramChatId))
                await TrySendAsync(row.UserId, row.Id, AssignedKind, null, channel, recipient, message, cancellationToken);
        }
    }

    /// <summary>
    /// Reminder number <paramref name="slot"/> (1-based) for <paramref name="today"/>: every
    /// assignment still not filled today gets one message per channel - at most once per slot,
    /// however often this runs. Returns how many were sent.
    /// </summary>
    public async Task<int> SendDueRemindersAsync(DateOnly today, int slot, CancellationToken cancellationToken)
    {
        if (!AnyChannelConfigured || !FormAssignmentTracker.IsWorkingDay(today)) return 0;

        var (assignments, stamps) = await FormAssignmentTracker.LoadAsync(_db, _dynamicData, null, null, today, today, cancellationToken);
        var pending = FormAssignmentTracker.BuildDays(assignments, stamps, today, today, today)
            .Where(d => d.Status == FormAssignmentTracker.Pending)
            .ToList();
        if (pending.Count == 0) return 0;

        var kind = ReminderKind(slot);
        var alreadySent = (await _db.NotificationLogs
                .Where(n => n.Kind == kind && n.ForDate == today && n.AssignmentId != null)
                .Select(n => new { AssignmentId = n.AssignmentId!.Value, n.Channel })
                .ToListAsync(cancellationToken))
            .Select(n => (n.AssignmentId, n.Channel))
            .ToHashSet();
        var userIds = pending.Select(d => d.UserId).Distinct().ToList();
        var contacts = await _db.Users
            .Where(u => userIds.Contains(u.Id) && u.IsActive && (u.PhoneNumber != null || u.TelegramChatId != null))
            .ToDictionaryAsync(u => u.Id, u => new { u.PhoneNumber, u.TelegramChatId }, cancellationToken);

        var sent = 0;
        foreach (var day in pending.Where(d => contacts.ContainsKey(d.UserId)))
        {
            var contact = contacts[day.UserId];
            var message = ReminderMessage(_whatsAppSettings.ReminderTemplate, day.UserName, day.FormName, today, Link($"/forms/{day.FormId}"));
            foreach (var (channel, recipient) in ChannelsFor(contact.PhoneNumber, contact.TelegramChatId))
            {
                if (alreadySent.Contains((day.AssignmentId, channel))) continue;
                if (await TrySendAsync(day.UserId, day.AssignmentId, kind, today, channel, recipient, message, cancellationToken))
                    sent++;
            }
        }
        return sent;
    }

    /// <summary>A sample message to one user on one channel, for checking the setup. Returns the outcome.</summary>
    public async Task<NotificationSendResult> SendTestAsync(Guid userId, string channel, CancellationToken cancellationToken)
    {
        var user = await _db.Users.SingleOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null) return new NotificationSendResult(false, null, "User not found.");

        string recipient;
        switch (channel)
        {
            case TelegramChannel:
                if (!_telegramSettings.IsConfigured)
                    return new NotificationSendResult(false, null, "Telegram isn't set up on this server (Telegram__BotToken / Telegram__BotUsername).");
                if (user.TelegramChatId is not { } chatId)
                    return new NotificationSendResult(false, null, "This user hasn't connected Telegram yet.");
                recipient = chatId.ToString(CultureInfo.InvariantCulture);
                break;
            case WhatsAppChannel:
                if (!_whatsAppSettings.IsConfigured)
                    return new NotificationSendResult(false, null, "WhatsApp isn't set up on this server (WhatsApp__PhoneNumberId / WhatsApp__AccessToken).");
                if (user.PhoneNumber is not { } phone)
                    return new NotificationSendResult(false, null, "This user has no mobile number.");
                recipient = phone;
                break;
            default:
                return new NotificationSendResult(false, null, $"Unknown channel '{channel}'.");
        }

        var log = NotificationLog.Start(userId, null, TestKind, null, channel, recipient, DateTime.UtcNow);
        _db.NotificationLogs.Add(log);
        await _db.SaveChangesAsync(cancellationToken);

        var today = FormAssignmentTracker.SaudiToday(DateTime.UtcNow);
        var message = TestMessage(_whatsAppSettings.ReminderTemplate, user.DisplayName, today, Link("/assignments"));
        NotificationSendResult result;
        try
        {
            result = await DeliverAsync(channel, recipient, message, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            result = new NotificationSendResult(false, null, ex.Message);
        }
        if (result.Success) log.MarkSent(result.MessageId, DateTime.UtcNow);
        else log.MarkFailed(result.Error ?? "unknown error");
        await _db.SaveChangesAsync(cancellationToken);
        return result;
    }

    private string Link(string path) => _settings.AppUrl.TrimEnd('/') + path;

    private IEnumerable<(string Channel, string Recipient)> ChannelsFor(string? phone, long? telegramChatId)
    {
        if (_telegramSettings.IsConfigured && telegramChatId is { } chatId)
            yield return (TelegramChannel, chatId.ToString(CultureInfo.InvariantCulture));
        if (_whatsAppSettings.IsConfigured && phone is not null)
            yield return (WhatsAppChannel, phone);
    }

    private Task<NotificationSendResult> DeliverAsync(string channel, string recipient, Message message, CancellationToken cancellationToken) =>
        channel == TelegramChannel
            // Telegram refuses a button whose address isn't a full http(s) URL (AppUrl unset).
            ? _telegram.SendMessageAsync(
                long.Parse(recipient, CultureInfo.InvariantCulture), message.Text,
                message.Link.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? message.ButtonText : null,
                message.Link.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? message.Link : null,
                cancellationToken)
            : _whatsApp.SendTemplateAsync(recipient, message.Template, message.TemplateParameters, cancellationToken);

    /// <summary>
    /// Claims the (assignment, kind, day, channel) slot by inserting its log row first; if
    /// another instance already did, the insert fails on the unique index and nothing is sent.
    /// </summary>
    private async Task<bool> TrySendAsync(
        Guid userId, Guid assignmentId, string kind, DateOnly? forDate, string channel, string recipient,
        Message message, CancellationToken cancellationToken)
    {
        var log = NotificationLog.Start(userId, assignmentId, kind, forDate, channel, recipient, DateTime.UtcNow);
        _db.NotificationLogs.Add(log);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Someone else claimed it - forget our copy so later saves don't retry it.
            if (_db is DbContext context) context.Entry(log).State = EntityState.Detached;
            return false;
        }

        NotificationSendResult result;
        try
        {
            result = await DeliverAsync(channel, recipient, message, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            result = new NotificationSendResult(false, null, ex.Message);
        }

        if (result.Success) log.MarkSent(result.MessageId, DateTime.UtcNow);
        else
        {
            log.MarkFailed(result.Error ?? "unknown error");
            _logger.LogWarning("{Channel} {Kind} to user {UserId} failed: {Error}", channel, kind, userId, result.Error);
        }
        await _db.SaveChangesAsync(cancellationToken);
        return result.Success;
    }
}
