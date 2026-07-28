using Platform.Domain.Common;

namespace Platform.Domain.Workflow;

public class WorkflowState : BaseEntity
{
    public Guid WorkflowDefinitionId { get; private set; }
    public string Code { get; private set; } = default!;
    public string Label { get; private set; } = default!;
    public bool IsInitial { get; private set; }
    public bool IsFinal { get; private set; }

    private WorkflowState() { }

    public static WorkflowState Create(Guid workflowDefinitionId, string code, string label, bool isInitial, bool isFinal) => new()
    {
        WorkflowDefinitionId = workflowDefinitionId,
        Code = code.Trim().ToLowerInvariant(),
        Label = label.Trim(),
        IsInitial = isInitial,
        IsFinal = isFinal
    };
}
