using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Platform.Application.Common.Interfaces;

namespace Platform.Application.Notifications;

/// <summary>
/// Links Telegram chats to users. A "Connect Telegram" link is t.me/{bot}?start={code}: the
/// person presses Start, Telegram sends the bot "/start {code}", and that chat becomes the
/// user's. "/stop" unlinks every user on that chat. Returns the reply the bot should send.
/// </summary>
public class TelegramLinker
{
    public static readonly TimeSpan LinkLifetime = TimeSpan.FromDays(7);

    private readonly IApplicationDbContext _db;

    public TelegramLinker(IApplicationDbContext db) => _db = db;

    /// <summary>32 URL-safe characters (Telegram allows A-Z a-z 0-9 _ - up to 64 in a start parameter).</summary>
    public static string NewCode() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)).Replace('+', '-').Replace('/', '_');

    /// <summary>"/start abc" -> ("start", "abc"); "/start@AsasBot abc" too; anything else -> (null, null).</summary>
    public static (string? Command, string? Argument) ParseCommand(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return (null, null);
        var trimmed = text.Trim();
        if (!trimmed.StartsWith('/')) return (null, null);
        var parts = trimmed[1..].Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return (null, null);
        var command = parts[0].Split('@')[0].ToLowerInvariant();
        return (command, parts.Length > 1 ? parts[1] : null);
    }

    public async Task<string?> HandleAsync(TelegramUpdate update, DateTime nowUtc, CancellationToken cancellationToken)
    {
        if (update.ChatId is not { } chatId) return null;
        var (command, argument) = ParseCommand(update.Text);

        switch (command)
        {
            case "start" when !string.IsNullOrWhiteSpace(argument):
            {
                var user = await _db.Users.SingleOrDefaultAsync(u => u.TelegramLinkCode == argument, cancellationToken);
                if (user is null || !user.HasOpenTelegramLink(argument, nowUtc))
                {
                    // Telegram re-sends an unconfirmed update after a restart; the code was
                    // already used on this chat, so say so instead of "invalid link".
                    if (await _db.Users.AnyAsync(u => u.TelegramChatId == chatId, cancellationToken))
                        return "حسابك مربوط بالفعل. ستصلك هنا تذكيرات النماذج المكلّف بها.";
                    return "هذا الرابط غير صالح أو انتهت صلاحيته. افتح «الإعدادات» في أساس واضغط «ربط تيليجرام» للحصول على رابط جديد.";
                }
                if (!user.IsActive)
                    return "هذا الحساب غير مفعّل في أساس.";

                user.LinkTelegram(chatId);
                await _db.SaveChangesAsync(cancellationToken);
                return $"تم ربط حسابك في أساس ({user.DisplayName}). ستصلك هنا تذكيرات النماذج المكلّف بها. لإيقافها أرسل /stop";
            }
            case "start":
                return await _db.Users.AnyAsync(u => u.TelegramChatId == chatId, cancellationToken)
                    ? "حسابك مربوط بالفعل. ستصلك هنا تذكيرات النماذج المكلّف بها. لإيقافها أرسل /stop"
                    : "مرحباً. لربط حسابك افتح «الإعدادات» في أساس واضغط «ربط تيليجرام».";
            case "stop":
            {
                var users = await _db.Users.Where(u => u.TelegramChatId == chatId).ToListAsync(cancellationToken);
                foreach (var u in users) u.UnlinkTelegram();
                if (users.Count > 0) await _db.SaveChangesAsync(cancellationToken);
                return users.Count > 0
                    ? "تم إلغاء الربط. لن تصلك تذكيرات هنا بعد الآن."
                    : "هذه المحادثة غير مربوطة بأي حساب.";
            }
            default:
                return "هذا البوت يرسل تذكيرات أساس فقط ولا يقرأ الرسائل.";
        }
    }
}
