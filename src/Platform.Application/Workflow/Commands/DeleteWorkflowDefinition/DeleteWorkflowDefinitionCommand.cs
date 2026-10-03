using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Platform.Application.Common.Exceptions;
using Platform.Application.Common.Interfaces;

namespace Platform.Application.Workflow.Commands.DeleteWorkflowDefinition;

/// <summary>
/// Closes a known gap (see CLAUDE.md's "Known gaps" section): until now, nothing could
/// delete a WorkflowDefinition, which meant any form it was attached to could never be
/// deleted either (DeleteFormCommand refuses outright while a workflow is attached).
///
/// Soft-delete, same as DeleteFormCommand's published-form branch: WorkflowDefinition is
/// already an AuditableEntity, already carries the platform-wide soft-delete query filter,
/// and its States/Transitions are small static-schema rows, not a physical dynamic table -
/// there's nothing to drop, so nothing is lost by leaving them physically in place. Once
/// soft-deleted, _db.WorkflowDefinitions (filtered) no longer sees it, so
/// DeleteFormCommand's "is a workflow attached" check clears on its own - no changes
/// needed there.
///
/// Refuses if any WorkflowInstance still exists for this workflow - deleting the
/// definition out from under a record genuinely mid-approval would orphan its state. A
/// workflow with real history isn't a case this command tries to handle; it's for cleaning
/// up a workflow nothing is actually using.
/// </summary>
public record DeleteWorkflowDefinitionCommand(Guid WorkflowDefinitionId) : IRequest;

public class DeleteWorkflowDefinitionCommandValidator : AbstractValidator<DeleteWorkflowDefinitionCommand>
{
    public DeleteWorkflowDefinitionCommandValidator() => RuleFor(x => x.WorkflowDefinitionId).NotEmpty();
}

public class DeleteWorkflowDefinitionCommandHandler : IRequestHandler<DeleteWorkflowDefinitionCommand>
{
    private readonly IApplicationDbContext _db;

    public DeleteWorkflowDefinitionCommandHandler(IApplicationDbContext db) => _db = db;

    public async Task Handle(DeleteWorkflowDefinitionCommand request, CancellationToken cancellationToken)
    {
        var workflow = await _db.WorkflowDefinitions
            .SingleOrDefaultAsync(w => w.Id == request.WorkflowDefinitionId, cancellationToken);

        if (workflow is null)
            throw new NotFoundException(nameof(Domain.Workflow.WorkflowDefinition), request.WorkflowDefinitionId);

        var instanceCount = await _db.WorkflowInstances
            .CountAsync(wi => wi.WorkflowDefinitionId == request.WorkflowDefinitionId, cancellationToken);

        if (instanceCount > 0)
            throw new Common.Exceptions.ValidationException(new[]
            {
                new FluentValidation.Results.ValidationFailure(nameof(request.WorkflowDefinitionId),
                    $"Can't delete - {instanceCount} record(s) have gone through this workflow. " +
                    "Deleting it would orphan their state.")
            });

        workflow.IsDeleted = true;
        await _db.SaveChangesAsync(cancellationToken);
    }
}
