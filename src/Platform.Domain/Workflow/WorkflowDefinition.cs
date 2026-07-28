using Platform.Domain.Common;

namespace Platform.Domain.Workflow;

/// <summary>
/// Attaches to exactly one form. No versioning for v1, unlike FormDefinition/FormVersion -
/// a workflow is Draft, then Published, then optionally Retired, full stop. Deliberate
/// scope cut: editing a published workflow's states/transitions isn't supported here.
/// Retire and create a new WorkflowDefinition instead if it needs to change.
/// </summary>
public class WorkflowDefinition : AuditableEntity
{
    public string Code { get; private set; } = default!;
    public string Name { get; private set; } = default!;
    public Guid FormDefinitionId { get; private set; }
    public WorkflowStatus Status { get; private set; } = WorkflowStatus.Draft;

    private readonly List<WorkflowState> _states = new();
    public IReadOnlyCollection<WorkflowState> States => _states.AsReadOnly();

    private readonly List<WorkflowTransition> _transitions = new();
    public IReadOnlyCollection<WorkflowTransition> Transitions => _transitions.AsReadOnly();

    private WorkflowDefinition() { }

    public static WorkflowDefinition Create(string code, string name, Guid formDefinitionId) => new()
    {
        Code = NormalizeCode(code),
        Name = name.Trim(),
        FormDefinitionId = formDefinitionId
    };

    public WorkflowState AddState(string code, string label, bool isInitial, bool isFinal)
    {
        RequireDraft();
        if (_states.Any(s => s.Code == code.Trim().ToLowerInvariant()))
            throw new InvalidOperationException($"State code '{code}' already exists on this workflow.");
        if (isInitial && _states.Any(s => s.IsInitial))
            throw new InvalidOperationException("A workflow can only have one initial state.");

        var state = WorkflowState.Create(Id, code, label, isInitial, isFinal);
        _states.Add(state);
        return state;
    }

    public WorkflowTransition AddTransition(string code, string label, Guid fromStateId, Guid toStateId, IEnumerable<Guid> allowedRoleIds)
    {
        RequireDraft();
        if (_states.All(s => s.Id != fromStateId) || _states.All(s => s.Id != toStateId))
            throw new InvalidOperationException("Both states must already exist on this workflow.");

        var transition = WorkflowTransition.Create(Id, code, label, fromStateId, toStateId, allowedRoleIds);
        _transitions.Add(transition);
        return transition;
    }

    public WorkflowState GetInitialState() =>
        _states.SingleOrDefault(s => s.IsInitial)
        ?? throw new InvalidOperationException($"Workflow '{Code}' has no initial state.");

    public void Publish()
    {
        RequireDraft();
        if (!_states.Any(s => s.IsInitial))
            throw new InvalidOperationException("Cannot publish a workflow with no initial state.");
        if (!_states.Any(s => s.IsFinal))
            throw new InvalidOperationException("Cannot publish a workflow with no final state.");
        if (_transitions.Count == 0)
            throw new InvalidOperationException("Cannot publish a workflow with no transitions.");

        var reachable = new HashSet<Guid> { GetInitialState().Id };
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var t in _transitions.Where(t => reachable.Contains(t.FromStateId) && !reachable.Contains(t.ToStateId)))
            {
                reachable.Add(t.ToStateId);
                changed = true;
            }
        }
        var unreachable = _states.Where(s => !reachable.Contains(s.Id)).ToList();
        if (unreachable.Count > 0)
            throw new InvalidOperationException(
                $"State(s) unreachable from the initial state: {string.Join(", ", unreachable.Select(s => s.Code))}.");

        Status = WorkflowStatus.Published;
    }

    private void RequireDraft()
    {
        if (Status != WorkflowStatus.Draft)
            throw new InvalidOperationException("This workflow is no longer a draft and can't be modified.");
    }

    private static string NormalizeCode(string code)
    {
        var slug = new string(code.Trim().ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray());
        while (slug.Contains("--")) slug = slug.Replace("--", "-");
        slug = slug.Trim('-');
        if (slug.Length == 0 || !char.IsLetter(slug[0]))
            throw new ArgumentException("Workflow code must start with a letter after normalization.");
        return slug;
    }
}
