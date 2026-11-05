namespace Platform.Application.Common.Interfaces;

/// <summary>
/// The "Telegram" configuration section (App Service settings Telegram__BotToken,
/// Telegram__BotUsername). Telegram is off until both are set. Staging and production need
/// <b>different bots</b>: the API reads the bot's messages by polling, and two servers polling
/// one bot take each other's messages (a link code then lands on the wrong database).
/// </summary>
public class TelegramSettings
{
    /// <summary>From @BotFather ("123456:ABC..."). A secret.</summary>
    public string? BotToken { get; set; }

    /// <summary>The bot's username without "@", e.g. AsasStagingBot - used to build t.me links.</summary>
    public string? BotUsername { get; set; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(BotToken) && !string.IsNullOrWhiteSpace(BotUsername);

    public string LinkFor(string code) => $"https://t.me/{BotUsername!.TrimStart('@')}?start={code}";
}

/// <summary>A text message someone sent the bot (other update kinds are skipped).</summary>
public record TelegramUpdate(long UpdateId, long? ChatId, string? Text);

/// <summary>The Telegram Bot API, called directly (free; no provider in between).</summary>
public interface ITelegramClient
{
    /// <summary>Plain text, plus an optional button that opens <paramref name="buttonUrl"/>.</summary>
    Task<NotificationSendResult> SendMessageAsync(
        long chatId, string text, string? buttonText, string? buttonUrl, CancellationToken cancellationToken = default);

    /// <summary>Long-polls for new messages; passing an offset confirms everything before it.</summary>
    Task<IReadOnlyList<TelegramUpdate>> GetUpdatesAsync(long offset, int timeoutSeconds, CancellationToken cancellationToken = default);
}
