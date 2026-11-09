using MediatR;
using Microsoft.EntityFrameworkCore;
using Platform.Application.Common.Exceptions;
using Platform.Application.Common.Interfaces;
using Platform.Application.Files.Commands.UploadFile;
using Platform.Domain.Forms;

namespace Platform.Application.Forms.Records;

/// <summary>
/// One submitted record with its attachments (My entries detail 08, Records drawer 20).
/// Allowed for its sender or an administrator - AND only with access to the record's form
/// (FormAccessBehavior, through IFormScopedRequest): a person who lost access to a form no
/// longer sees what they sent on it (owner decision, 2026-11-06). Anyone else gets 403.
/// </summary>
public record GetRecordDetailQuery(Guid FormDefinitionId, Guid RecordId) : IRequest<RecordDetailDto>, IFormScopedRequest;

public class GetRecordDetailQueryHandler : IRequestHandler<GetRecordDetailQuery, RecordDetailDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IDynamicDataRepository _repository;
    private readonly ICurrentUserService _currentUser;

    public GetRecordDetailQueryHandler(IApplicationDbContext db, IDynamicDataRepository repository, ICurrentUserService currentUser)
    {
        _db = db;
        _repository = repository;
        _currentUser = currentUser;
    }

    public async Task<RecordDetailDto> Handle(GetRecordDetailQuery request, CancellationToken cancellationToken)
    {
        var form = await _db.FormDefinitions.Include(f => f.Versions).ThenInclude(v => v.Fields)
            .SingleOrDefaultAsync(f => f.Id == request.FormDefinitionId, cancellationToken)
            ?? throw new NotFoundException(nameof(FormDefinition), request.FormDefinitionId);
        var version = form.GetPublishedVersion();
        if (version is null || form.TableName is null) throw new NotFoundException("Record", request.RecordId);
        var fields = version.Fields.Where(f => f.IsActive).ToList();

        var row = await _repository.GetRecordAsync(form.TableName, fields, request.RecordId, cancellationToken)
            ?? throw new NotFoundException("Record", request.RecordId);

        var isAdmin = _currentUser.Roles.Contains("Administrator", StringComparer.OrdinalIgnoreCase);
        if (!isAdmin && row.CreatedByUserId != _currentUser.UserId)
            throw new ForbiddenAccessException("Only the person who sent this record, or an administrator, can open it.");

        var item = (await RecordViews.ToItemsAsync(_db, _repository, form.Id, fields, new[] { row }, cancellationToken))[0];
        var files = await _db.FileMetadataEntries
            .Where(f => f.RecordId == row.Id)
            .OrderBy(f => f.CreatedAtUtc)
            .Select(f => new FileMetadataDto(f.Id, f.FieldCode, f.OriginalFileName, f.ContentType, f.SizeBytes, f.CreatedAtUtc))
            .ToListAsync(cancellationToken);
        return new RecordDetailDto(item, files);
    }
}
