using Platform.Domain.Common;

namespace Platform.Domain.Forms;

/// <summary>
/// A submission sent with an idempotency key and/or the phone's own time (redesign §9.6, the
/// offline queue). Static schema - never a column on a form's data table.
///
/// The row is inserted BEFORE the data row: its unique (UserId, IdempotencyKey) index is the
/// "this entry is saved only once" lock, so a phone that retries after a lost answer gets the
/// original record back instead of a duplicate (same pattern as NotificationLog's send-once).
/// EnteredAtUtc is when the person pressed Send on the phone (owner decision 8): CreatedAtUtc on
/// the data row stays the server's time; this one decides the assignment day and is displayed.
/// </summary>
public class SubmissionReceipt : AuditableEntity
{
    public Guid UserId { get; private set; }
    public Guid FormDefinitionId { get; private set; }
    public string IdempotencyKey { get; private set; } = default!;
    /// <summary>Null while the data row is being written.</summary>
    public Guid? RecordId { get; private set; }
    public DateTime? EnteredAtUtc { get; private set; }

    private SubmissionReceipt() { }

    public static SubmissionReceipt Claim(Guid userId, Guid formDefinitionId, string idempotencyKey, DateTime? enteredAtUtc) => new()
    {
        UserId = userId,
        FormDefinitionId = formDefinitionId,
        IdempotencyKey = idempotencyKey,
        EnteredAtUtc = enteredAtUtc is { } e ? DateTime.SpecifyKind(e, DateTimeKind.Utc) : null,
    };

    public void Complete(Guid recordId) => RecordId = recordId;
}
