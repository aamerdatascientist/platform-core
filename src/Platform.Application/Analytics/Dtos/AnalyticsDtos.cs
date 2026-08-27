namespace Platform.Application.Analytics.Dtos;

public record WorkflowStateCountDto(Guid StateId, string StateCode, string StateLabel, bool IsFinal, int InstanceCount);

public record StateDurationDto(
    Guid StateId, string StateCode, string StateLabel, DateTime EnteredAtUtc, DateTime? ExitedAtUtc, TimeSpan Duration);

public record WorkflowInstanceTimeInStateDto(Guid WorkflowInstanceId, Guid RecordId, IReadOnlyList<StateDurationDto> StateDurations);

public record ModuleSubmissionCountDto(string ModuleName, IReadOnlyList<FormSubmissionCountDto> Forms);

public record FormSubmissionCountDto(Guid FormDefinitionId, string FormCode, string FormName, int SubmissionCount);
