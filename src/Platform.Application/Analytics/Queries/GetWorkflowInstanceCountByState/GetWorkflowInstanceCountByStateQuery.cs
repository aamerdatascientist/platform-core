using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Platform.Application.Analytics.Dtos;
using Platform.Application.Common.Exceptions;
using Platform.Application.Common.Interfaces;
using Platform.Domain.Workflow;

namespace Platform.Application.Analytics.Queries.GetWorkflowInstanceCountByState;

public record GetWorkflowInstanceCountByStateQuery(Guid WorkflowDefinitionId) : IRequest<IReadOnlyList<WorkflowStateCountDto>>;

public class GetWorkflowInstanceCountByStateQueryValidator : AbstractValidator<GetWorkflowInstanceCountByStateQuery>
{
    public GetWorkflowInstanceCountByStateQueryValidator()
    {
        RuleFor(x => x.WorkflowDefinitionId).NotEmpty();
    }
}

public class GetWorkflowInstanceCountByStateQueryHandler
    : IRequestHandler<GetWorkflowInstanceCountByStateQuery, IReadOnlyList<WorkflowStateCountDto>>
{
    private readonly IApplicationDbContext _db;

    public GetWorkflowInstanceCountByStateQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<IReadOnlyList<WorkflowStateCountDto>> Handle(
        GetWorkflowInstanceCountByStateQuery request, CancellationToken cancellationToken)
    {
        var states = await _db.WorkflowStates
            .Where(s => s.WorkflowDefinitionId == request.WorkflowDefinitionId)
            .ToListAsync(cancellationToken);

        if (states.Count == 0)
            throw new NotFoundException(nameof(WorkflowDefinition), request.WorkflowDefinitionId);

        // GROUP BY on the DB side, not an in-memory count - this is exactly what the new
        // IX_WorkflowInstances_CurrentStateId/FormDefinitionId indexes exist to support.
        var countsByState = await _db.WorkflowInstances
            .Where(i => i.WorkflowDefinitionId == request.WorkflowDefinitionId)
            .GroupBy(i => i.CurrentStateId)
            .Select(g => new { StateId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.StateId, g => g.Count, cancellationToken);

        // States with zero live instances are still returned, at 0 - a "count by state"
        // dashboard chart needs every possible state represented, not just the active ones.
        return states
            .Select(s => new WorkflowStateCountDto(
                s.Id, s.Code, s.Label, s.IsFinal, countsByState.GetValueOrDefault(s.Id, 0)))
            .OrderByDescending(s => s.InstanceCount)
            .ToList();
    }
}
