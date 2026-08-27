using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Platform.Application.Analytics.Dtos;
using Platform.Application.Common.Exceptions;
using Platform.Application.Common.Interfaces;
using Platform.Domain.Workflow;

namespace Platform.Application.Analytics.Queries.GetWorkflowTimeInState;

public record GetWorkflowTimeInStateQuery(Guid WorkflowDefinitionId) : IRequest<IReadOnlyList<WorkflowInstanceTimeInStateDto>>;

public class GetWorkflowTimeInStateQueryValidator : AbstractValidator<GetWorkflowTimeInStateQuery>
{
    public GetWorkflowTimeInStateQueryValidator()
    {
        RuleFor(x => x.WorkflowDefinitionId).NotEmpty();
    }
}

public class GetWorkflowTimeInStateQueryHandler
    : IRequestHandler<GetWorkflowTimeInStateQuery, IReadOnlyList<WorkflowInstanceTimeInStateDto>>
{
    private readonly IApplicationDbContext _db;

    public GetWorkflowTimeInStateQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<IReadOnlyList<WorkflowInstanceTimeInStateDto>> Handle(
        GetWorkflowTimeInStateQuery request, CancellationToken cancellationToken)
    {
        var statesById = await _db.WorkflowStates
            .Where(s => s.WorkflowDefinitionId == request.WorkflowDefinitionId)
            .ToDictionaryAsync(s => s.Id, cancellationToken);

        if (statesById.Count == 0)
            throw new NotFoundException(nameof(WorkflowDefinition), request.WorkflowDefinitionId);

        // The composite (WorkflowInstanceId, ExecutedAtUtc) index backs both this filter and
        // the in-memory OrderBy below - History is loaded once per instance, not re-queried.
        var instances = await _db.WorkflowInstances
            .Include(i => i.History)
            .Where(i => i.WorkflowDefinitionId == request.WorkflowDefinitionId)
            .ToListAsync(cancellationToken);

        var now = DateTime.UtcNow;
        var result = new List<WorkflowInstanceTimeInStateDto>();

        foreach (var instance in instances)
        {
            var orderedHistory = instance.History.OrderBy(h => h.ExecutedAtUtc).ToList();
            var durations = new List<StateDurationDto>();

            for (var i = 0; i < orderedHistory.Count; i++)
            {
                var entry = orderedHistory[i];
                var state = statesById[entry.ToStateId];

                // An instance is "in" ToState from this entry's timestamp until the next
                // transition's timestamp - or until now, for the most recent entry, since
                // that's the state the instance is still sitting in.
                var exitedAtUtc = i + 1 < orderedHistory.Count ? orderedHistory[i + 1].ExecutedAtUtc : (DateTime?)null;

                durations.Add(new StateDurationDto(
                    state.Id, state.Code, state.Label, entry.ExecutedAtUtc, exitedAtUtc, (exitedAtUtc ?? now) - entry.ExecutedAtUtc));
            }

            result.Add(new WorkflowInstanceTimeInStateDto(instance.Id, instance.RecordId, durations));
        }

        return result;
    }
}
