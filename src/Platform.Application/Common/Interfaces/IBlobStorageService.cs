namespace Platform.Application.Common.Interfaces;

public interface IBlobStorageService
{
    /// <summary>Uploads content under the given blob name (path/key within the container).</summary>
    Task UploadAsync(string blobName, Stream content, string contentType, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a time-limited, signed URL for downloading a private blob - never a
    /// permanent public link, since the container itself has no anonymous access.
    /// </summary>
    Task<string> GetReadUrlAsync(string blobName, TimeSpan validFor, CancellationToken cancellationToken = default);

    Task DeleteAsync(string blobName, CancellationToken cancellationToken = default);
}
