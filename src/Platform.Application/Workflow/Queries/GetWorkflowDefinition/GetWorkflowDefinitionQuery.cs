using MediatR;
using Microsoft.EntityFrameworkCore;
using Platform.Application.Common.Exceptions;
using Platform.Application.Common.Interfaces;
using Platform.Application.Workflow.Dtos;

namespace Platform.Application.Workflow.Queries.GetWorkflowDefinition;

public record GetWorkflowDefinitionQuery(Guid WorkflowDefinitionId) : IRequest<WorkflowDefinitionDto>;

public class GetWorkflowDefinitionQueryHandler : IRequestHandler<GetWorkflowDefinitionQuery, WorkflowDefinitionDto>
{
    private readonly IApplicationDbContext _db;

    public GetWorkflowDefinitionQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<WorkflowDefinitionDto> Handle(GetWorkflowDefinitionQuery request, CancellationToken cancellationToken)
    {
        var workflow = await _db.WorkflowDefinitions
            .Include(w => w.States)
            .Include(w => w.Transitions).ThenInclude(t => t.AllowedRoles)
            .SingleOrDefaultAsync(w => w.Id == request.WorkflowDefinitionId, cancellationToken);

        if (workflow is null)
            throw new NotFoundException(nameof(Platform.Domain.Workflow.WorkflowDefinition), request.WorkflowDefinitionId);

        var states = workflow.States
            .Select(s => new WorkflowStateDto(s.Id, s.Code, s.Label, s.IsInitial, s.IsFinal))
            .ToList();

        var transitions = workflow.Transitions
            .Select(t => new WorkflowTransitionDto(
                t.Id, t.Code, t.Label, t.FromStateId, t.ToStateId,
                t.AllowedRoles.Select(ar => ar.RoleId).ToList()))
            .ToList();

        return new WorkflowDefinitionDto(workflow.Id, workflow.Code, workflow.Name, workflow.FormDefinitionId, workflow.Status, states, transitions);
    }
}
