using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Platform.Application.Common.Exceptions;
using Platform.Application.Common.Interfaces;

namespace Platform.Application.Forms.Commands.DeleteSubmission;

/// <summary>
/// Removes a submitted record (administrators only - enforced on the endpoint), e.g. a
/// duplicate or a test entry. A soft delete: the row is marked deleted, so it disappears from
/// the records list, the lookup choices and the dashboard, but its values stay in the table
/// and can be brought back with SQL if it was a mistake. Its attachments and workflow history
/// are left as they are.
/// </summary>
public record DeleteSubmissionCommand(Guid FormDefinitionId, Guid RecordId, Guid DeletedByUserId)
    : IRequest, IFormScopedRequest;

public class DeleteSubmissionCommandValidator : AbstractValidator<DeleteSubmissionCommand>
{
    public DeleteSubmissionCommandValidator()
    {
        RuleFor(x => x.FormDefinitionId).NotEmpty();
        RuleFor(x => x.RecordId).NotEmpty();
        RuleFor(x => x.DeletedByUserId).NotEmpty();
    }
}

public class DeleteSubmissionCommandHandler : IRequestHandler<DeleteSubmissionCommand>
{
    private readonly IApplicationDbContext _db;
    private readonly IDynamicDataRepository _dynamicDataRepository;

    public DeleteSubmissionCommandHandler(IApplicationDbContext db, IDynamicDataRepository dynamicDataRepository)
    {
        _db = db;
        _dynamicDataRepository = dynamicDataRepository;
    }

    public async Task Handle(DeleteSubmissionCommand request, CancellationToken cancellationToken)
    {
        var tableName = await _db.FormDefinitions
            .Where(f => f.Id == request.FormDefinitionId)
            .Select(f => f.TableName)
            .SingleOrDefaultAsync(cancellationToken);

        if (tableName is null) throw new NotFoundException("Record", request.RecordId);

        var deleted = await _dynamicDataRepository.SoftDeleteAsync(
            tableName, request.RecordId, request.DeletedByUserId, cancellationToken);

        if (!deleted) throw new NotFoundException("Record", request.RecordId);
    }
}
