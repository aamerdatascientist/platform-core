using MediatR;
using Microsoft.EntityFrameworkCore;
using Platform.Application.Common.Interfaces;
using Platform.Application.Files.Commands.UploadFile;

namespace Platform.Application.Files.Queries.GetFilesForRecord;

public record GetFilesForRecordQuery(Guid RecordId) : IRequest<IReadOnlyList<FileMetadataDto>>;

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
