using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Platform.Application.Common.Interfaces;

namespace Platform.Infrastructure.Notifications;

/// <summary>
/// Meta's WhatsApp Cloud API, called directly (no reseller, so no platform fee): one POST per
/// template message to graph.facebook.com/{version}/{phone-number-id}/messages.
/// </summary>
public class WhatsAppSender : IWhatsAppSender
{
    private readonly HttpClient _http;
    private readonly WhatsAppSettings _settings;

    public WhatsAppSender(HttpClient http, WhatsAppSettings settings)
    {
        _http = http;
        _settings = settings;
    }

    public async Task<NotificationSendResult> SendTemplateAsync(
        string to, string templateName, IReadOnlyList<string> bodyParameters, CancellationToken cancellationToken = default)
    {
        if (!_settings.IsConfigured) return new NotificationSendResult(false, null, "WhatsApp isn't configured.");

        var payload = new
        {
            messaging_product = "whatsapp",
            to,
            type = "template",
            template = new
            {
                name = templateName,
                language = new { code = _settings.TemplateLanguage },
                components = new[]
                {
                    new
                    {
                        type = "body",
                        // WhatsApp rejects a parameter with a newline, a tab or 4+ spaces in a row.
                        parameters = bodyParameters.Select(p => new { type = "text", text = Clean(p) }).ToArray(),
                    },
                },
            },
        };

        using var request = new HttpRequestMessage(
            HttpMethod.Post, $"https://graph.facebook.com/{_settings.ApiVersion}/{_settings.PhoneNumberId}/messages")
        {
            Content = JsonContent.Create(payload),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settings.AccessToken);

        using var response = await _http.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        try
        {
            using var json = JsonDocument.Parse(body);
            if (response.IsSuccessStatusCode &&
                json.RootElement.TryGetProperty("messages", out var messages) && messages.GetArrayLength() > 0)
                return new NotificationSendResult(true, messages[0].GetProperty("id").GetString(), null);

            if (json.RootElement.TryGetProperty("error", out var error))
            {
                var message = error.TryGetProperty("message", out var m) ? m.GetString() : null;
                var details = error.TryGetProperty("error_data", out var d) && d.TryGetProperty("details", out var dd) ? dd.GetString() : null;
                var code = error.TryGetProperty("code", out var c) ? c.ToString() : null;
                return new NotificationSendResult(false, null, $"{(int)response.StatusCode} ({code}): {message}{(details is null ? "" : " - " + details)}");
            }
        }
        catch (JsonException)
        {
        }
        return new NotificationSendResult(false, null, $"{(int)response.StatusCode}: {Truncate(body, 300)}");
    }

    private static string Clean(string text) =>
        System.Text.RegularExpressions.Regex.Replace(text.Replace('\n', ' ').Replace('\t', ' '), " {2,}", " ").Trim();

    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..max];
}
