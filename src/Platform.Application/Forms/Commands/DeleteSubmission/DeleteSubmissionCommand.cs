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
    private readonly IFormSubmissionHooks _hooks;

    public DeleteSubmissionCommandHandler(IApplicationDbContext db, IDynamicDataRepository dynamicDataRepository, IFormSubmissionHooks hooks)
    {
        _db = db;
        _dynamicDataRepository = dynamicDataRepository;
        _hooks = hooks;
    }

    public async Task Handle(DeleteSubmissionCommand request, CancellationToken cancellationToken)
    {
        var form = await _db.FormDefinitions.SingleOrDefaultAsync(f => f.Id == request.FormDefinitionId, cancellationToken);
        var tableName = form?.TableName;
        if (tableName is null) throw new NotFoundException("Record", request.RecordId);

        // A module can refuse (e.g. a stage-plan row that reports point at - owner: never delete, deactivate).
        foreach (var hook in _hooks.For(form!.Code)) await hook.BeforeDeleteAsync(form, request.RecordId, cancellationToken);

        var deleted = await _dynamicDataRepository.SoftDeleteAsync(
            tableName, request.RecordId, request.DeletedByUserId, cancellationToken);

        if (!deleted) throw new NotFoundException("Record", request.RecordId);
    }
}
