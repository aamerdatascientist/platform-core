using Platform.Domain.Common;

namespace Platform.Domain.Files;

/// <summary>
/// The binary content lives in Blob Storage, never in SQL - this row is purely metadata
/// plus the blob's storage key. Deliberately keyed by RecordId (a dynamic table row's
/// GUID) rather than a foreign key to any specific dynamic table, same reasoning as
/// WorkflowInstance - a GUID primary key is globally unique on its own, so no ambiguity
/// about which form's record this belongs to even without a real FK.
/// </summary>
public class FileMetadata : AuditableEntity
{
    public Guid FormDefinitionId { get; private set; }
    public Guid RecordId { get; private set; }
    public string FieldCode { get; private set; } = default!;

    /// <summary>The blob's path/key within the container - not the same as the original filename.</summary>
    public string BlobName { get; private set; } = default!;
    public string OriginalFileName { get; private set; } = default!;
    public string ContentType { get; private set; } = default!;
    public long SizeBytes { get; private set; }

    private FileMetadata() { }

    public static FileMetadata Create(
        Guid formDefinitionId, Guid recordId, string fieldCode,
        string blobName, string originalFileName, string contentType, long sizeBytes) => new()
    {
        FormDefinitionId = formDefinitionId,
        RecordId = recordId,
        FieldCode = fieldCode,
        BlobName = blobName,
        OriginalFileName = originalFileName,
        ContentType = contentType,
        SizeBytes = sizeBytes
    };
}
