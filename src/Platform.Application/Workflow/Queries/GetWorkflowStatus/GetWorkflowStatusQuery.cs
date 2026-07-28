using MediatR;
using Microsoft.EntityFrameworkCore;
using Platform.Application.Common.Exceptions;
using Platform.Application.Common.Interfaces;
using Platform.Application.Workflow.Dtos;

namespace Platform.Application.Workflow.Queries.GetWorkflowStatus;

public record GetWorkflowStatusQuery(Guid RecordId) : IRequest<WorkflowStatusDto>;

public class GetWorkflowStatusQueryHandler : IRequestHandler<GetWorkflowStatusQuery, WorkflowStatusDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public GetWorkflowStatusQueryHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<WorkflowStatusDto> Handle(GetWorkflowStatusQuery request, CancellationToken cancellationToken)
    {
        var instance = await _db.WorkflowInstances
            .Include(i => i.History)
            .SingleOrDefaultAsync(i => i.RecordId == request.RecordId, cancellationToken);

        if (instance is null)
            throw new NotFoundException(nameof(Domain.Workflow.WorkflowInstance), request.RecordId);

        var workflow = await _db.WorkflowDefinitions
            .Include(w => w.States)
            .Include(w => w.Transitions).ThenInclude(t => t.AllowedRoles)
            .SingleAsync(w => w.Id == instance.WorkflowDefinitionId, cancellationToken);

        var statesById = workflow.States.ToDictionary(s => s.Id);
        var currentState = statesById[instance.CurrentStateId];

        var roleIdsInvolved = workflow.Transitions.SelectMany(t => t.AllowedRoles.Select(ar => ar.RoleId)).Distinct().ToList();
        var roleNamesById = await _db.Roles
            .Where(r => roleIdsInvolved.Contains(r.Id))
            .ToDictionaryAsync(r => r.Id, r => r.Name, cancellationToken);

        var availableTransitions = workflow.Transitions
            .Where(t => t.FromStateId == instance.CurrentStateId)
            .Where(t => t.AllowedRoles
                .Select(ar => roleNamesById.GetValueOrDefault(ar.RoleId))
                .Any(name => name != null && _currentUser.Roles.Contains(name, StringComparer.OrdinalIgnoreCase)))
            .Select(t => new AvailableTransitionDto(t.Code, t.Label))
            .ToList();

        var transitionsById = workflow.Transitions.ToDictionary(t => t.Id);

        var history = instance.History
            .OrderBy(h => h.ExecutedAtUtc)
            .Select(h => new WorkflowHistoryEntryDto(
                h.FromStateId.HasValue ? statesById[h.FromStateId.Value].Label : null,
                statesById[h.ToStateId].Label,
                h.TransitionId.HasValue && transitionsById.TryGetValue(h.TransitionId.Value, out var t) ? t.Label : null,
                h.ExecutedByUserId,
                h.ExecutedAtUtc,
                h.Comment))
            .ToList();

        return new WorkflowStatusDto(
            instance.RecordId, workflow.Code, currentState.Code, currentState.Label, currentState.IsFinal,
            availableTransitions, history);
    }
}
