using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Platform.Application.Common.Exceptions;
using Platform.Application.Common.Interfaces;
using Platform.Domain.Files;
using Platform.Domain.Forms.Enums;

namespace Platform.Application.Files.Commands.UploadFile;

public record UploadFileCommand(
    Guid FormDefinitionId, Guid RecordId, string FieldCode,
    Stream Content, string OriginalFileName, string ContentType, long SizeBytes, Guid UploadedByUserId)
    : IRequest<FileMetadataDto>, IFormScopedRequest;

public record FileMetadataDto(
    Guid Id, string FieldCode, string OriginalFileName, string ContentType, long SizeBytes, DateTime CreatedAtUtc);

public class UploadFileCommandValidator : AbstractValidator<UploadFileCommand>
{
    // 20MB - generous for site/inspection photos and scanned documents, not so generous
    // that one bad upload meaningfully dents storage costs. Adjust here if that's wrong
    // for real usage once this has been used for a while.
    private const long MaxSizeBytes = 20 * 1024 * 1024;

    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg", "image/png", "image/heic", "image/webp", "application/pdf",
    };

    public UploadFileCommandValidator()
    {
        RuleFor(x => x.FormDefinitionId).NotEmpty();
        RuleFor(x => x.RecordId).NotEmpty();
        RuleFor(x => x.FieldCode).NotEmpty();
        RuleFor(x => x.OriginalFileName).NotEmpty().MaximumLength(260);
        RuleFor(x => x.SizeBytes).LessThanOrEqualTo(MaxSizeBytes)
            .WithMessage($"File must be under {MaxSizeBytes / (1024 * 1024)}MB.");
        RuleFor(x => x.ContentType).Must(ct => AllowedContentTypes.Contains(ct))
            .WithMessage("Only JPEG, PNG, HEIC, WEBP images and PDF files are accepted.");
    }
}

public class UploadFileCommandHandler : IRequestHandler<UploadFileCommand, FileMetadataDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IBlobStorageService _blobStorage;

    public UploadFileCommandHandler(IApplicationDbContext db, IBlobStorageService blobStorage)
    {
        _db = db;
        _blobStorage = blobStorage;
    }

    public async Task<FileMetadataDto> Handle(UploadFileCommand request, CancellationToken cancellationToken)
    {
        var formDefinition = await _db.FormDefinitions
            .Include(f => f.Versions).ThenInclude(v => v.Fields)
            .SingleOrDefaultAsync(f => f.Id == request.FormDefinitionId, cancellationToken);

        if (formDefinition is null)
            throw new NotFoundException(nameof(Platform.Domain.Forms.FormDefinition), request.FormDefinitionId);

        var publishedVersion = formDefinition.GetPublishedVersion();
        var field = publishedVersion?.Fields.SingleOrDefault(f => f.Code == request.FieldCode && f.IsActive);

        // Validating the field is a real, active Attachment field on this form's published
        // version - not just any string the caller happened to send - is the actual
        // security boundary here, same spirit as SqlTypeMapper.AssertSafeIdentifier for
        // the Form Engine: never trust a client-supplied field code without checking it
        // against known metadata first.
        if (field is null || field.FieldType != FieldType.Attachment)
            throw new Common.Exceptions.ValidationException(new[]
            {
                new FluentValidation.Results.ValidationFailure(
                    nameof(request.FieldCode), $"'{request.FieldCode}' isn't a valid attachment field on this form.")
            });

        // Buffered fully rather than peeked-and-reset: request.Content isn't guaranteed
        // seekable (depends on how ASP.NET Core backed the multipart body for this
        // request), and the size cap above already accepts holding one upload's worth in
        // memory. This is also where the actual bytes get checked against the declared
        // Content-Type - UploadFileCommandValidator's allow-list only ever validated that
        // header string, which the caller controls and can lie about.
        var buffer = new MemoryStream();
        await request.Content.CopyToAsync(buffer, cancellationToken);
        var header = buffer.Length <= 4096 ? buffer.ToArray() : buffer.ToArray()[..4096];
        if (!FileSignatureValidator.MatchesDeclaredContentType(header, request.ContentType))
            throw new Common.Exceptions.ValidationException(new[]
            {
                new FluentValidation.Results.ValidationFailure(
                    nameof(request.ContentType), "The file's actual content doesn't match its declared type.")
            });
        buffer.Position = 0;

        var blobName = $"{formDefinition.Code}/{request.RecordId}/{request.FieldCode}/{Guid.NewGuid()}_{SanitizeFileName(request.OriginalFileName)}";

        await _blobStorage.UploadAsync(blobName, buffer, request.ContentType, cancellationToken);

        var metadata = FileMetadata.Create(
            formDefinition.Id, request.RecordId, request.FieldCode,
            blobName, request.OriginalFileName, request.ContentType, request.SizeBytes);

        _db.FileMetadataEntries.Add(metadata);
        await _db.SaveChangesAsync(cancellationToken);

        return new FileMetadataDto(
            metadata.Id, metadata.FieldCode, metadata.OriginalFileName, metadata.ContentType,
            metadata.SizeBytes, metadata.CreatedAtUtc);
    }

    private static string SanitizeFileName(string fileName)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(fileName.Where(c => !invalid.Contains(c)).ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? "file" : cleaned;
    }
}
