using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Platform.Application.Common.Exceptions;
using Platform.Application.Common.Interfaces;

namespace Platform.Application.Workflow.Commands.AddWorkflowState;

public record AddWorkflowStateCommand(Guid WorkflowDefinitionId, string Code, string Label, bool IsInitial, bool IsFinal) : IRequest<Guid>;

public class AddWorkflowStateCommandValidator : AbstractValidator<AddWorkflowStateCommand>
{
    public AddWorkflowStateCommandValidator()
    {
        RuleFor(x => x.WorkflowDefinitionId).NotEmpty();
        RuleFor(x => x.Code).NotEmpty().MaximumLength(64);
        RuleFor(x => x.Label).NotEmpty().MaximumLength(200);
        RuleFor(x => x).Must(x => !(x.IsInitial && x.IsFinal))
            .WithMessage("A state can't be both initial and final.");
    }
}

public class AddWorkflowStateCommandHandler : IRequestHandler<AddWorkflowStateCommand, Guid>
{
    private readonly IApplicationDbContext _db;

    public AddWorkflowStateCommandHandler(IApplicationDbContext db) => _db = db;

    public async Task<Guid> Handle(AddWorkflowStateCommand request, CancellationToken cancellationToken)
    {
        var workflow = await _db.WorkflowDefinitions
            .Include(w => w.States)
            .SingleOrDefaultAsync(w => w.Id == request.WorkflowDefinitionId, cancellationToken);

        if (workflow is null)
            throw new NotFoundException(nameof(Platform.Domain.Workflow.WorkflowDefinition), request.WorkflowDefinitionId);

        var state = workflow.AddState(request.Code, request.Label, request.IsInitial, request.IsFinal);
        await _db.SaveChangesAsync(cancellationToken);

        return state.Id;
    }
}
