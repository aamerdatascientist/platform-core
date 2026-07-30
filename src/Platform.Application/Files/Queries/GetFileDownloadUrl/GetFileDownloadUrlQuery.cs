using MediatR;
using Microsoft.EntityFrameworkCore;
using Platform.Application.Common.Exceptions;
using Platform.Application.Common.Interfaces;

namespace Platform.Application.Files.Queries.GetFileDownloadUrl;

public record GetFileDownloadUrlQuery(Guid FileId) : IRequest<string>;

public class GetFileDownloadUrlQueryHandler : IRequestHandler<GetFileDownloadUrlQuery, string>
{
    private readonly IApplicationDbContext _db;
    private readonly IBlobStorageService _blobStorage;

    public GetFileDownloadUrlQueryHandler(IApplicationDbContext db, IBlobStorageService blobStorage)
    {
        _db = db;
        _blobStorage = blobStorage;
    }

    public async Task<string> Handle(GetFileDownloadUrlQuery request, CancellationToken cancellationToken)
    {
        var file = await _db.FileMetadataEntries
            .SingleOrDefaultAsync(f => f.Id == request.FileId && !f.IsDeleted, cancellationToken);

        if (file is null)
            throw new NotFoundException(nameof(Domain.Files.FileMetadata), request.FileId);

        // 10 minutes is enough to load an image or start a download without the link
        // staying valid indefinitely if it ever leaks (a shared screenshot, a browser
        // history entry on a shared computer).
        return await _blobStorage.GetReadUrlAsync(file.BlobName, TimeSpan.FromMinutes(10), cancellationToken);
    }
}
