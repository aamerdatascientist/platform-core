using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Platform.Application.Common.Exceptions;
using Platform.Application.Common.Interfaces;

namespace Platform.Application.Workflow.Commands.AddWorkflowTransition;

public record AddWorkflowTransitionCommand(
    Guid WorkflowDefinitionId, string Code, string Label, Guid FromStateId, Guid ToStateId, IReadOnlyList<Guid> AllowedRoleIds)
    : IRequest<Guid>;

public class AddWorkflowTransitionCommandValidator : AbstractValidator<AddWorkflowTransitionCommand>
{
    public AddWorkflowTransitionCommandValidator()
    {
        RuleFor(x => x.WorkflowDefinitionId).NotEmpty();
        RuleFor(x => x.Code).NotEmpty().MaximumLength(64);
        RuleFor(x => x.Label).NotEmpty().MaximumLength(200);
        RuleFor(x => x.FromStateId).NotEmpty();
        RuleFor(x => x.ToStateId).NotEmpty();
        RuleFor(x => x.AllowedRoleIds).NotEmpty()
            .WithMessage("A transition needs at least one allowed role.");
    }
}

public class AddWorkflowTransitionCommandHandler : IRequestHandler<AddWorkflowTransitionCommand, Guid>
{
    private readonly IApplicationDbContext _db;

    public AddWorkflowTransitionCommandHandler(IApplicationDbContext db) => _db = db;

    public async Task<Guid> Handle(AddWorkflowTransitionCommand request, CancellationToken cancellationToken)
    {
        var workflow = await _db.WorkflowDefinitions
            .Include(w => w.States)
            .Include(w => w.Transitions)
            .SingleOrDefaultAsync(w => w.Id == request.WorkflowDefinitionId, cancellationToken);

        if (workflow is null)
            throw new NotFoundException(nameof(Platform.Domain.Workflow.WorkflowDefinition), request.WorkflowDefinitionId);

        var roleIds = await _db.Roles
            .Where(r => request.AllowedRoleIds.Contains(r.Id))
            .Select(r => r.Id)
            .ToListAsync(cancellationToken);

        if (roleIds.Count != request.AllowedRoleIds.Distinct().Count())
            throw new Common.Exceptions.ValidationException(new[]
            {
                new FluentValidation.Results.ValidationFailure(
                    nameof(request.AllowedRoleIds), "One or more allowed role IDs don't exist.")
            });

        var transition = workflow.AddTransition(request.Code, request.Label, request.FromStateId, request.ToStateId, roleIds);
        await _db.SaveChangesAsync(cancellationToken);

        return transition.Id;
    }
}
