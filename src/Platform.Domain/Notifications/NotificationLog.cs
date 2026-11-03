using Platform.Domain.Common;

namespace Platform.Domain.Notifications;

/// <summary>
/// One message sent (or tried) to a user - today only WhatsApp. The row is written BEFORE the
/// message goes out, under a unique (AssignmentId, Kind, ForDate) key, so two API instances or
/// a restart can never send the same reminder twice: whoever inserts the row sends it.
/// </summary>
public class NotificationLog : BaseEntity
{
    public const string Sending = "sending";
    public const string Sent = "sent";
    public const string Failed = "failed";

    public Guid UserId { get; private set; }
    public Guid? AssignmentId { get; private set; }

    /// <summary>"assigned", "reminder-1", "reminder-2", ... or "test".</summary>
    public string Kind { get; private set; } = default!;

    /// <summary>The Saudi day a reminder is about; null for one-off messages.</summary>
    public DateOnly? ForDate { get; private set; }

    public string Channel { get; private set; } = default!;
    public string Recipient { get; private set; } = default!;
    public string Status { get; private set; } = default!;
    public string? ProviderMessageId { get; private set; }
    public string? Error { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? SentAtUtc { get; private set; }

    private NotificationLog() { }

    public static NotificationLog Start(
        Guid userId, Guid? assignmentId, string kind, DateOnly? forDate, string channel, string recipient, DateTime nowUtc) => new()
    {
        UserId = userId,
        AssignmentId = assignmentId,
        Kind = kind,
        ForDate = forDate,
        Channel = channel,
        Recipient = recipient,
        Status = Sending,
        CreatedAtUtc = nowUtc,
    };

    public void MarkSent(string? providerMessageId, DateTime nowUtc)
    {
        Status = Sent;
        ProviderMessageId = providerMessageId;
        SentAtUtc = nowUtc;
    }

    public void MarkFailed(string error)
    {
        Status = Failed;
        Error = error.Length > 1000 ? error[..1000] : error;
    }
}
