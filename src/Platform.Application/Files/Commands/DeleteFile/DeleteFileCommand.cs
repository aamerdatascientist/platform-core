using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Platform.Application.Common.Exceptions;
using Platform.Application.Common.Interfaces;

namespace Platform.Application.Files.Commands.DeleteFile;

public record DeleteFileCommand(Guid FileId) : IRequest;

public class DeleteFileCommandValidator : AbstractValidator<DeleteFileCommand>
{
    public DeleteFileCommandValidator() => RuleFor(x => x.FileId).NotEmpty();
}

public class DeleteFileCommandHandler : IRequestHandler<DeleteFileCommand>
{
    private readonly IApplicationDbContext _db;
    private readonly IBlobStorageService _blobStorage;

    public DeleteFileCommandHandler(IApplicationDbContext db, IBlobStorageService blobStorage)
    {
        _db = db;
        _blobStorage = blobStorage;
    }

    public async Task Handle(DeleteFileCommand request, CancellationToken cancellationToken)
    {
        var file = await _db.FileMetadataEntries
            .SingleOrDefaultAsync(f => f.Id == request.FileId && !f.IsDeleted, cancellationToken);

        if (file is null)
            throw new NotFoundException(nameof(Domain.Files.FileMetadata), request.FileId);

        await _blobStorage.DeleteAsync(file.BlobName, cancellationToken);

        file.IsDeleted = true;
        await _db.SaveChangesAsync(cancellationToken);
    }
}
