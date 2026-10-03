using MediatR;
using Microsoft.EntityFrameworkCore;
using Platform.Application.Common.Interfaces;
using Platform.Domain.Workflow;

namespace Platform.Application.Workflow.Queries.GetWorkflowsList;

/// <summary>Lightweight on purpose - same spirit as GetFormsListQuery. No per-item access
/// filtering (workflows don't have an AllowedRoles/AllowedUsers model like forms do);
/// the controller's own [Authorize] is the only gate, same as the single-workflow Get.</summary>
public record WorkflowSummaryDto(Guid Id, string Code, string Name, Guid FormDefinitionId, WorkflowStatus Status);

public record GetWorkflowsListQuery : IRequest<IReadOnlyList<WorkflowSummaryDto>>;

public class GetWorkflowsListQueryHandler : IRequestHandler<GetWorkflowsListQuery, IReadOnlyList<WorkflowSummaryDto>>
{
    private readonly IApplicationDbContext _db;

    public GetWorkflowsListQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<IReadOnlyList<WorkflowSummaryDto>> Handle(GetWorkflowsListQuery request, CancellationToken cancellationToken) =>
        await _db.WorkflowDefinitions
            .OrderBy(w => w.Name)
            .Select(w => new WorkflowSummaryDto(w.Id, w.Code, w.Name, w.FormDefinitionId, w.Status))
            .ToListAsync(cancellationToken);
}
