namespace Platform.Application.Common.Interfaces;

/// <summary>
/// For a request that operates on one form but is only keyed by something else (a FileId, a
/// RecordId) - FormAccessBehavior can't read FormDefinitionId directly off the request the
/// way it does for IFormScopedRequest, so the request resolves it itself via a lookup. Each
/// domain knows its own path to a FormDefinitionId (FileMetadata carries it directly;
/// WorkflowInstance.RecordId -> FormDefinitionId), so that knowledge stays with the request,
/// not in the (deliberately domain-agnostic) behavior.
///
/// Return null when there's nothing to resolve yet (e.g. GetFilesForRecordQuery for a record
/// with zero files so far) - the behavior treats that as "nothing to check, nothing to leak"
/// and lets the request through; whatever not-found/empty-result handling the handler already
/// has still applies normally.
/// </summary>
public interface IFormScopeResolvingRequest
{
    Task<Guid?> ResolveFormDefinitionIdAsync(IApplicationDbContext db, CancellationToken cancellationToken);
}
