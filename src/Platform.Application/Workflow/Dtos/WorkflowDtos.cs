using Platform.Domain.Workflow;

namespace Platform.Application.Workflow.Dtos;

public record WorkflowStateDto(Guid Id, string Code, string Label, bool IsInitial, bool IsFinal);

public record WorkflowTransitionDto(Guid Id, string Code, string Label, Guid FromStateId, Guid ToStateId, IReadOnlyList<Guid> AllowedRoleIds);

public record WorkflowDefinitionDto(
    Guid Id, string Code, string Name, Guid FormDefinitionId, WorkflowStatus Status,
    IReadOnlyList<WorkflowStateDto> States, IReadOnlyList<WorkflowTransitionDto> Transitions);

public record AvailableTransitionDto(string Code, string Label);

public record WorkflowHistoryEntryDto(
    string? FromStateLabel, string ToStateLabel, string? TransitionLabel, Guid ExecutedByUserId, DateTime ExecutedAtUtc, string? Comment);

public record WorkflowStatusDto(
    Guid RecordId, string WorkflowCode, string CurrentStateCode, string CurrentStateLabel, bool IsFinal,
    IReadOnlyList<AvailableTransitionDto> AvailableTransitions, IReadOnlyList<WorkflowHistoryEntryDto> History);
