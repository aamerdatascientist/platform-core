using Platform.Domain.Common;

namespace Platform.Domain.Workflow;

public class WorkflowInstanceHistoryEntry : BaseEntity
{
    public Guid WorkflowInstanceId { get; private set; }
    public Guid? FromStateId { get; private set; }
    public Guid ToStateId { get; private set; }
    public Guid? TransitionId { get; private set; }
    public Guid ExecutedByUserId { get; private set; }
    public DateTime ExecutedAtUtc { get; private set; }
    public string? Comment { get; private set; }

    private WorkflowInstanceHistoryEntry() { }

    public static WorkflowInstanceHistoryEntry Create(
        Guid workflowInstanceId, Guid? fromStateId, Guid toStateId, Guid? transitionId, Guid executedByUserId, string? comment) => new()
    {
        WorkflowInstanceId = workflowInstanceId,
        FromStateId = fromStateId,
        ToStateId = toStateId,
        TransitionId = transitionId,
        ExecutedByUserId = executedByUserId,
        ExecutedAtUtc = DateTime.UtcNow,
        Comment = comment
    };
}
