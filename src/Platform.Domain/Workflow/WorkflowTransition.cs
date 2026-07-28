using Platform.Domain.Common;

namespace Platform.Domain.Workflow;

/// <summary>
/// AllowedRoles is deliberately a list of Role IDs, not role names - names can change,
/// IDs can't. Resolving which role names those IDs correspond to happens in the
/// Application layer at execution time (ExecuteWorkflowTransitionCommandHandler), where
/// a Roles lookup is natural; the domain entity itself has no reason to know about names.
/// </summary>
public class WorkflowTransition : BaseEntity
{
    public Guid WorkflowDefinitionId { get; private set; }
    public string Code { get; private set; } = default!;
    public string Label { get; private set; } = default!;
    public Guid FromStateId { get; private set; }
    public Guid ToStateId { get; private set; }

    private readonly List<WorkflowTransitionRole> _allowedRoles = new();
    public IReadOnlyCollection<WorkflowTransitionRole> AllowedRoles => _allowedRoles.AsReadOnly();

    private WorkflowTransition() { }

    public static WorkflowTransition Create(
        Guid workflowDefinitionId, string code, string label, Guid fromStateId, Guid toStateId, IEnumerable<Guid> allowedRoleIds)
    {
        if (fromStateId == toStateId)
            throw new ArgumentException("A transition must move to a different state.");

        var transition = new WorkflowTransition
        {
            WorkflowDefinitionId = workflowDefinitionId,
            Code = code.Trim().ToLowerInvariant(),
            Label = label.Trim(),
            FromStateId = fromStateId,
            ToStateId = toStateId
        };

        foreach (var roleId in allowedRoleIds.Distinct())
            transition._allowedRoles.Add(WorkflowTransitionRole.Create(transition.Id, roleId));

        if (transition._allowedRoles.Count == 0)
            throw new ArgumentException("A transition needs at least one allowed role - otherwise nobody could ever execute it.");

        return transition;
    }
}

public class WorkflowTransitionRole
{
    public Guid WorkflowTransitionId { get; private set; }
    public Guid RoleId { get; private set; }

    private WorkflowTransitionRole() { }

    public static WorkflowTransitionRole Create(Guid workflowTransitionId, Guid roleId) => new()
    {
        WorkflowTransitionId = workflowTransitionId,
        RoleId = roleId
    };
}
