using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Platform.Application.Common.Interfaces;
using Platform.Application.Forms.Assignments;
using Platform.Domain.Notifications;

namespace Platform.Application.Notifications;

/// <summary>
/// WhatsApp messages about form assignments: one when a user is assigned, and reminders on
/// working days to anyone who hasn't filled today's form yet. Every message is logged in
/// NotificationLogs, and a failure is logged there too - sending never breaks the request or
/// the scheduler that triggered it. Users without a phone number are skipped.
/// </summary>
public class AssignmentNotifier
{
    public const string Channel = "whatsapp";
    public const string AssignedKind = "assigned";
    public const string TestKind = "test";
    public static string ReminderKind(int slot) => $"reminder-{slot}";

    private static readonly string[] ArabicMonths =
        { "يناير", "فبراير", "مارس", "أبريل", "مايو", "يونيو", "يوليو", "أغسطس", "سبتمبر", "أكتوبر", "نوفمبر", "ديسمبر" };

    private readonly IApplicationDbContext _db;
    private readonly IDynamicDataRepository _dynamicData;
    private readonly IWhatsAppSender _sender;
    private readonly WhatsAppSettings _settings;
    private readonly ILogger<AssignmentNotifier> _logger;

    public AssignmentNotifier(
        IApplicationDbContext db, IDynamicDataRepository dynamicData, IWhatsAppSender sender,
        WhatsAppSettings settings, ILogger<AssignmentNotifier> logger)
    {
        _db = db;
        _dynamicData = dynamicData;
        _sender = sender;
        _settings = settings;
        _logger = logger;
    }

    /// <summary>"3 نوفمبر 2026" - the Gregorian date in Arabic (ar-SA's own calendar is Hijri).</summary>
    public static string ArabicDate(DateOnly date) => $"{date.Day} {ArabicMonths[date.Month - 1]} {date.Year}";

    /// <summary>Tells each newly assigned user. Called right after the assignments are saved.</summary>
    public async Task NotifyAssignedAsync(IReadOnlyCollection<Guid> assignmentIds, CancellationToken cancellationToken)
    {
        if (!_settings.IsConfigured || assignmentIds.Count == 0) return;

        var rows = await _db.FormAssignments
            .Where(a => assignmentIds.Contains(a.Id))
            .Join(_db.Users, a => a.UserId, u => u.Id, (a, u) => new { a.Id, a.UserId, a.FormDefinitionId, a.StartDate, u.DisplayName, u.PhoneNumber })
            .Join(_db.FormDefinitions, x => x.FormDefinitionId, f => f.Id, (x, f) => new { x.Id, x.UserId, x.StartDate, x.DisplayName, x.PhoneNumber, FormName = f.Name })
            .ToListAsync(cancellationToken);

        foreach (var row in rows.Where(r => r.PhoneNumber is not null))
        {
            await TrySendAsync(
                row.UserId, row.Id, AssignedKind, null, row.PhoneNumber!, _settings.AssignedTemplate,
                new[] { row.DisplayName, row.FormName, ArabicDate(row.StartDate), Link("/assignments") },
                cancellationToken);
        }
    }

    /// <summary>
    /// Reminder number <paramref name="slot"/> (1-based) for <paramref name="today"/>: every
    /// assignment still not filled today gets one message - at most once per slot, however
    /// often this runs. Returns how many were sent.
    /// </summary>
    public async Task<int> SendDueRemindersAsync(DateOnly today, int slot, CancellationToken cancellationToken)
    {
        if (!_settings.IsConfigured || !FormAssignmentTracker.IsWorkingDay(today)) return 0;

        var (assignments, stamps) = await FormAssignmentTracker.LoadAsync(_db, _dynamicData, null, null, today, today, cancellationToken);
        var pending = FormAssignmentTracker.BuildDays(assignments, stamps, today, today, today)
            .Where(d => d.Status == FormAssignmentTracker.Pending)
            .ToList();
        if (pending.Count == 0) return 0;

        var kind = ReminderKind(slot);
        var alreadySent = await _db.NotificationLogs
            .Where(n => n.Kind == kind && n.ForDate == today && n.AssignmentId != null)
            .Select(n => n.AssignmentId!.Value)
            .ToListAsync(cancellationToken);
        var userIds = pending.Select(d => d.UserId).Distinct().ToList();
        var phones = await _db.Users
            .Where(u => userIds.Contains(u.Id) && u.IsActive && u.PhoneNumber != null)
            .ToDictionaryAsync(u => u.Id, u => u.PhoneNumber!, cancellationToken);

        var sent = 0;
        foreach (var day in pending.Where(d => !alreadySent.Contains(d.AssignmentId) && phones.ContainsKey(d.UserId)))
        {
            if (await TrySendAsync(
                    day.UserId, day.AssignmentId, kind, today, phones[day.UserId], _settings.ReminderTemplate,
                    new[] { day.UserName, day.FormName, ArabicDate(today), Link($"/forms/{day.FormId}") },
                    cancellationToken))
                sent++;
        }
        return sent;
    }

    /// <summary>A sample reminder to one user, for checking the setup. Returns the outcome.</summary>
    public async Task<WhatsAppSendResult> SendTestAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (!_settings.IsConfigured)
            return new WhatsAppSendResult(false, null, "WhatsApp isn't set up on this server (WhatsApp__PhoneNumberId / WhatsApp__AccessToken).");

        var user = await _db.Users.SingleOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user?.PhoneNumber is not { } phone)
            return new WhatsAppSendResult(false, null, "This user has no mobile number.");

        var log = NotificationLog.Start(userId, null, TestKind, null, Channel, phone, DateTime.UtcNow);
        _db.NotificationLogs.Add(log);
        await _db.SaveChangesAsync(cancellationToken);

        var today = FormAssignmentTracker.SaudiToday(DateTime.UtcNow);
        var result = await _sender.SendTemplateAsync(
            phone, _settings.ReminderTemplate,
            new[] { user.DisplayName, "اختبار", ArabicDate(today), Link("/assignments") }, cancellationToken);
        if (result.Success) log.MarkSent(result.MessageId, DateTime.UtcNow);
        else log.MarkFailed(result.Error ?? "unknown error");
        await _db.SaveChangesAsync(cancellationToken);
        return result;
    }

    private string Link(string path) => _settings.AppUrl.TrimEnd('/') + path;

    /// <summary>
    /// Claims the (assignment, kind, day) slot by inserting its log row first; if another
    /// instance already did, the insert fails on the unique index and nothing is sent.
    /// </summary>
    private async Task<bool> TrySendAsync(
        Guid userId, Guid assignmentId, string kind, DateOnly? forDate, string phone, string template,
        IReadOnlyList<string> parameters, CancellationToken cancellationToken)
    {
        var log = NotificationLog.Start(userId, assignmentId, kind, forDate, Channel, phone, DateTime.UtcNow);
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

        WhatsAppSendResult result;
        try
        {
            result = await _sender.SendTemplateAsync(phone, template, parameters, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            result = new WhatsAppSendResult(false, null, ex.Message);
        }

        if (result.Success) log.MarkSent(result.MessageId, DateTime.UtcNow);
        else
        {
            log.MarkFailed(result.Error ?? "unknown error");
            _logger.LogWarning("WhatsApp {Kind} to user {UserId} failed: {Error}", kind, userId, result.Error);
        }
        await _db.SaveChangesAsync(cancellationToken);
        return result.Success;
    }
}
