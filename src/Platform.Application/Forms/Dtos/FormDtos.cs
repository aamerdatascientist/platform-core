using Platform.Domain.Forms.Enums;

namespace Platform.Application.Forms.Dtos;

public record FieldDefinitionDto(
    Guid Id, string Code, string Label, FieldType FieldType, bool IsRequired, bool IsActive,
    int DisplayOrder, string? OptionsJson, Guid? LookupFormDefinitionId, string? ValidationRulesJson,
    string? VisibleWhenFieldCode, string? VisibleWhenValuesJson, string? FilterByFieldCode,
    Guid? DynamicOptionsSourceFormDefinitionId = null, string? DynamicOptionsSourceFieldCode = null,
    string? LabelEn = null, Guid? StepId = null);

/// <summary>A filling step (redesign §9.2), in DisplayOrder.</summary>
public record FormStepDto(Guid Id, string TitleAr, string? TitleEn, int DisplayOrder);

public record FormVersionDto(Guid Id, int VersionNumber, FormStatus Status, DateTime? PublishedAtUtc,
    IReadOnlyList<FieldDefinitionDto> Fields, IReadOnlyList<FormStepDto>? Steps = null);

public record FormDefinitionDto(
    Guid Id, string Code, string Name, string? Description, string ModuleName,
    FormStatus Status, string? TableName, FormVersionDto? DraftVersion, FormVersionDto? PublishedVersion,
    IReadOnlyList<Guid> AllowedRoleIds, IReadOnlyList<Guid> AllowedUserIds, string? NameEn = null);

public record NewFieldDto(
    string Code, string Label, FieldType FieldType, bool IsRequired,
    string? OptionsJson, Guid? LookupFormDefinitionId, string? ValidationRulesJson,
    string? VisibleWhenFieldCode = null, string? VisibleWhenValuesJson = null,
    string? FilterByFieldCode = null, string? LabelEn = null, Guid? StepId = null);
