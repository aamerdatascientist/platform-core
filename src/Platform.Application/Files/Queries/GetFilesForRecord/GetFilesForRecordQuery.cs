using MediatR;
using Microsoft.EntityFrameworkCore;
using Platform.Application.Common.Interfaces;
using Platform.Application.Files.Commands.UploadFile;

namespace Platform.Application.Files.Queries.GetFilesForRecord;

public record GetFilesForRecordQuery(Guid RecordId) : IRequest<IReadOnlyList<FileMetadataDto>>, IFormScopeResolvingRequest
{
    // Null when this record has no files yet - nothing to check access against, and an
    // empty result discloses nothing anyway, so the request is let through to the
    // handler's own (already-correct) empty-list behavior.
    public async Task<Guid?> ResolveFormDefinitionIdAsync(IApplicationDbContext db, CancellationToken cancellationToken) =>
        await db.FileMetadataEntries
            .Where(f => f.RecordId == RecordId)
            .Select(f => (Guid?)f.FormDefinitionId)
            .FirstOrDefaultAsync(cancellationToken);
}

public class GetFilesForRecordQueryHandler : IRequestHandler<GetFilesForRecordQuery, IReadOnlyList<FileMetadataDto>>
{
    private readonly IApplicationDbContext _db;

    public GetFilesForRecordQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<IReadOnlyList<FileMetadataDto>> Handle(GetFilesForRecordQuery request, CancellationToken cancellationToken) =>
        await _db.FileMetadataEntries
            .Where(f => f.RecordId == request.RecordId && !f.IsDeleted)
            .OrderBy(f => f.CreatedAtUtc)
            .Select(f => new FileMetadataDto(f.Id, f.FieldCode, f.OriginalFileName, f.ContentType, f.SizeBytes, f.CreatedAtUtc))
            .ToListAsync(cancellationToken);
}
