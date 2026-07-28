using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Platform.Application.Common.Exceptions;
using Platform.Application.Common.Interfaces;

namespace Platform.Application.Workflow.Commands.ExecuteWorkflowTransition;

public record ExecuteWorkflowTransitionCommand(Guid RecordId, string TransitionCode, string? Comment) : IRequest;

public class ExecuteWorkflowTransitionCommandValidator : AbstractValidator<ExecuteWorkflowTransitionCommand>
{
    public ExecuteWorkflowTransitionCommandValidator()
    {
        RuleFor(x => x.RecordId).NotEmpty();
        RuleFor(x => x.TransitionCode).NotEmpty();
    }
}

public class ExecuteWorkflowTransitionCommandHandler : IRequestHandler<ExecuteWorkflowTransitionCommand>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public ExecuteWorkflowTransitionCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task Handle(ExecuteWorkflowTransitionCommand request, CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is null)
            throw new ForbiddenAccessException("No authenticated user.");

        var instance = await _db.WorkflowInstances
            .SingleOrDefaultAsync(i => i.RecordId == request.RecordId, cancellationToken);

        if (instance is null)
            throw new NotFoundException(nameof(Domain.Workflow.WorkflowInstance), request.RecordId);

        var workflow = await _db.WorkflowDefinitions
            .Include(w => w.Transitions).ThenInclude(t => t.AllowedRoles)
            .SingleAsync(w => w.Id == instance.WorkflowDefinitionId, cancellationToken);

        var transitionCode = request.TransitionCode.Trim().ToLowerInvariant();
        var transition = workflow.Transitions.SingleOrDefault(
            t => t.Code == transitionCode && t.FromStateId == instance.CurrentStateId);

        if (transition is null)
            throw new Common.Exceptions.ValidationException(new[]
            {
                new FluentValidation.Results.ValidationFailure(
                    nameof(request.TransitionCode),
                    $"'{request.TransitionCode}' isn't a valid transition from the record's current state.")
            });

        // The JWT carries role NAMES (see JwtTokenService), but WorkflowTransitionRole stores
        // role IDs (names can be renamed, IDs can't) - so permission-checking means resolving
        // one to the other here, not in the domain entity, which has no reason to know about names.
        var allowedRoleIds = transition.AllowedRoles.Select(ar => ar.RoleId).ToList();
        var allowedRoleNames = await _db.Roles
            .Where(r => allowedRoleIds.Contains(r.Id))
            .Select(r => r.Name)
            .ToListAsync(cancellationToken);

        var callerHasPermission = _currentUser.Roles.Any(
            callerRole => allowedRoleNames.Contains(callerRole, StringComparer.OrdinalIgnoreCase));

        if (!callerHasPermission)
            throw new ForbiddenAccessException(
                $"You don't have a role permitted to execute '{transition.Label}' on this record.");

        instance.ApplyTransition(transition.Id, transition.ToStateId, _currentUser.UserId.Value, request.Comment);
        await _db.SaveChangesAsync(cancellationToken);
    }
}
