using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Platform.Application.Common.Exceptions;
using Platform.Application.Common.Interfaces;

namespace Platform.Application.Workflow.Commands.PublishWorkflowDefinition;

public record PublishWorkflowDefinitionCommand(Guid WorkflowDefinitionId) : IRequest;

public class PublishWorkflowDefinitionCommandValidator : AbstractValidator<PublishWorkflowDefinitionCommand>
{
    public PublishWorkflowDefinitionCommandValidator() => RuleFor(x => x.WorkflowDefinitionId).NotEmpty();
}

public class PublishWorkflowDefinitionCommandHandler : IRequestHandler<PublishWorkflowDefinitionCommand>
{
    private readonly IApplicationDbContext _db;

    public PublishWorkflowDefinitionCommandHandler(IApplicationDbContext db) => _db = db;

    public async Task Handle(PublishWorkflowDefinitionCommand request, CancellationToken cancellationToken)
    {
        var workflow = await _db.WorkflowDefinitions
            .Include(w => w.States)
            .Include(w => w.Transitions)
            .SingleOrDefaultAsync(w => w.Id == request.WorkflowDefinitionId, cancellationToken);

        if (workflow is null)
            throw new NotFoundException(nameof(Platform.Domain.Workflow.WorkflowDefinition), request.WorkflowDefinitionId);

        // Also worth checking here, not just at the domain layer: does this form already
        // have another published workflow? Two active workflows on the same form would be
        // ambiguous about which one starts on submit. One published workflow per form, enforced here.
        var formAlreadyHasPublishedWorkflow = await _db.WorkflowDefinitions.AnyAsync(
            w => w.FormDefinitionId == workflow.FormDefinitionId
                 && w.Id != workflow.Id
                 && w.Status == Domain.Workflow.WorkflowStatus.Published,
            cancellationToken);

        if (formAlreadyHasPublishedWorkflow)
            throw new Common.Exceptions.ValidationException(new[]
            {
                new FluentValidation.Results.ValidationFailure(
                    nameof(request.WorkflowDefinitionId), "This form already has a published workflow. Retire it first.")
            });

        workflow.Publish();
        await _db.SaveChangesAsync(cancellationToken);
    }
}
