using Platform.Domain.Common;

namespace Platform.Domain.Workflow;

public class WorkflowInstance : AuditableEntity
{
    public Guid WorkflowDefinitionId { get; private set; }
    public Guid FormDefinitionId { get; private set; }

    /// <summary>Id of the row in the form's dynamic table this instance tracks. Globally
    /// unique on its own (a GUID primary key), so no ambiguity about which form it belongs
    /// to even without FormDefinitionId - that field is kept for query convenience only.</summary>
    public Guid RecordId { get; private set; }
    public Guid CurrentStateId { get; private set; }

    private readonly List<WorkflowInstanceHistoryEntry> _history = new();
    public IReadOnlyCollection<WorkflowInstanceHistoryEntry> History => _history.AsReadOnly();

    private WorkflowInstance() { }

    public static WorkflowInstance Start(Guid workflowDefinitionId, Guid formDefinitionId, Guid recordId, Guid initialStateId, Guid startedByUserId)
    {
        var instance = new WorkflowInstance
        {
            WorkflowDefinitionId = workflowDefinitionId,
            FormDefinitionId = formDefinitionId,
            RecordId = recordId,
            CurrentStateId = initialStateId
        };
        instance._history.Add(WorkflowInstanceHistoryEntry.Create(
            instance.Id, null, initialStateId, null, startedByUserId, "Workflow started"));
        return instance;
    }

    public WorkflowInstanceHistoryEntry ApplyTransition(Guid transitionId, Guid toStateId, Guid executedByUserId, string? comment)
    {
        var fromStateId = CurrentStateId;
        CurrentStateId = toStateId;
        var entry = WorkflowInstanceHistoryEntry.Create(Id, fromStateId, toStateId, transitionId, executedByUserId, comment);
        _history.Add(entry);
        return entry;
    }
}
