namespace Platform.Application.Common.Interfaces;

/// <summary>
/// The "WhatsApp" configuration section (App Service settings WhatsApp__PhoneNumberId etc.).
/// WhatsApp is off until both <see cref="PhoneNumberId"/> and <see cref="AccessToken"/> are
/// set - every send is then skipped, nothing fails.
/// </summary>
public class WhatsAppSettings
{
    /// <summary>Meta's id for the sending number (WhatsApp Manager > Phone numbers), not the number itself.</summary>
    public string? PhoneNumberId { get; set; }

    /// <summary>A permanent System User token with whatsapp_business_messaging. A secret.</summary>
    public string? AccessToken { get; set; }

    public string ApiVersion { get; set; } = "v23.0";

    /// <summary>Language code the templates were approved in.</summary>
    public string TemplateLanguage { get; set; } = "ar";

    /// <summary>Body: {{1}} name, {{2}} form, {{3}} start date, {{4}} link.</summary>
    public string AssignedTemplate { get; set; } = "form_assigned";

    /// <summary>Body: {{1}} name, {{2}} form, {{3}} date, {{4}} link.</summary>
    public string ReminderTemplate { get; set; } = "form_reminder";

    /// <summary>The site the links in messages point to, e.g. https://staging.asasksa.co.</summary>
    public string AppUrl { get; set; } = "";

    /// <summary>Saudi times ("HH:mm") on working days to remind anyone who hasn't filled today's form.</summary>
    public string[] ReminderTimes { get; set; } = { "09:00", "14:00" };

    public bool IsConfigured => !string.IsNullOrWhiteSpace(PhoneNumberId) && !string.IsNullOrWhiteSpace(AccessToken);
}

public record WhatsAppSendResult(bool Success, string? MessageId, string? Error);

/// <summary>Sends an approved WhatsApp template message through Meta's Cloud API.</summary>
public interface IWhatsAppSender
{
    /// <param name="to">International digits without "+", e.g. 9665XXXXXXXX.</param>
    /// <param name="bodyParameters">The template body's {{1}}, {{2}}, ... in order.</param>
    Task<WhatsAppSendResult> SendTemplateAsync(
        string to, string templateName, IReadOnlyList<string> bodyParameters, CancellationToken cancellationToken = default);
}
