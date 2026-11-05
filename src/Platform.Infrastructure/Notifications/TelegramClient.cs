using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Platform.Application.Common.Interfaces;

namespace Platform.Infrastructure.Notifications;

/// <summary>
/// The Telegram Bot API (api.telegram.org/bot{token}/{method}), called directly. Free, no
/// provider in between. The token is part of the URL, so it never goes into a log line or
/// an error message from here.
/// </summary>
public class TelegramClient : ITelegramClient
{
    /// <summary>Per message; the HttpClient's own timeout is longer, for long polling.</summary>
    private static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(15);

    private readonly HttpClient _http;
    private readonly TelegramSettings _settings;

    public TelegramClient(HttpClient http, TelegramSettings settings)
    {
        _http = http;
        _settings = settings;
    }

    private string Url(string method) => $"https://api.telegram.org/bot{_settings.BotToken}/{method}";

    public async Task<NotificationSendResult> SendMessageAsync(
        long chatId, string text, string? buttonText, string? buttonUrl, CancellationToken cancellationToken = default)
    {
        if (!_settings.IsConfigured) return new NotificationSendResult(false, null, "Telegram isn't configured.");

        object payload = buttonText is not null && buttonUrl is not null
            ? new
            {
                chat_id = chatId,
                text,
                reply_markup = new { inline_keyboard = new[] { new[] { new { text = buttonText, url = buttonUrl } } } },
            }
            : new { chat_id = chatId, text };

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(SendTimeout);
        HttpResponseMessage response;
        try
        {
            response = await _http.PostAsJsonAsync(Url("sendMessage"), payload, timeout.Token);
        }
        catch (HttpRequestException ex)
        {
            return new NotificationSendResult(false, null, $"Telegram unreachable: {ex.HttpRequestError}");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new NotificationSendResult(false, null, "Telegram didn't answer in time.");
        }

        using (response)
        {
            var (ok, result, description) = await ReadAsync(response, cancellationToken);
            if (ok && result.TryGetProperty("message_id", out var id))
                return new NotificationSendResult(true, id.GetInt64().ToString(CultureInfo.InvariantCulture), null);
            // e.g. 403 "Forbidden: bot was blocked by the user", 400 "Bad Request: chat not found".
            return new NotificationSendResult(false, null, $"{(int)response.StatusCode}: {description ?? "unexpected answer"}");
        }
    }

    public async Task<IReadOnlyList<TelegramUpdate>> GetUpdatesAsync(
        long offset, int timeoutSeconds, CancellationToken cancellationToken = default)
    {
        var url = $"{Url("getUpdates")}?offset={offset}&timeout={timeoutSeconds}&allowed_updates=%5B%22message%22%5D";
        using var response = await _http.GetAsync(url, cancellationToken);
        var (ok, result, description) = await ReadAsync(response, cancellationToken);
        if (!ok)
            throw new TelegramApiException((int)response.StatusCode, description ?? "unexpected answer");

        var updates = new List<TelegramUpdate>();
        foreach (var u in result.EnumerateArray())
        {
            var updateId = u.GetProperty("update_id").GetInt64();
            long? chatId = null;
            string? text = null;
            var isGroup = false;
            long? migrateTo = null;
            if (u.TryGetProperty("message", out var message))
            {
                // Private chats and groups; channels are not served.
                if (message.TryGetProperty("chat", out var chat) && chat.TryGetProperty("type", out var type))
                {
                    var kind = type.GetString();
                    if (kind is "private" or "group" or "supergroup")
                    {
                        chatId = chat.GetProperty("id").GetInt64();
                        isGroup = kind != "private";
                    }
                }
                if (message.TryGetProperty("text", out var t)) text = t.GetString();
                if (message.TryGetProperty("migrate_to_chat_id", out var m)) migrateTo = m.GetInt64();
            }
            updates.Add(new TelegramUpdate(updateId, chatId, text, isGroup, migrateTo));
        }
        return updates;
    }

    private static async Task<(bool Ok, JsonElement Result, string? Description)> ReadAsync(
        HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        try
        {
            using var json = JsonDocument.Parse(body);
            var root = json.RootElement;
            var ok = root.TryGetProperty("ok", out var okEl) && okEl.ValueKind == JsonValueKind.True;
            var description = root.TryGetProperty("description", out var d) ? d.GetString() : null;
            var result = root.TryGetProperty("result", out var r) ? r.Clone() : default;
            return (ok, result, description);
        }
        catch (JsonException)
        {
            return (false, default, body.Length <= 200 ? body : body[..200]);
        }
    }
}

public class TelegramApiException : Exception
{
    public int StatusCode { get; }

    public TelegramApiException(int statusCode, string description) : base($"Telegram {statusCode}: {description}") =>
        StatusCode = statusCode;
}
