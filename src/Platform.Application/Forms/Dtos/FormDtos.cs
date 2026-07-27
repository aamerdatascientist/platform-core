using Platform.Domain.Forms.Enums;

namespace Platform.Application.Forms.Dtos;

public record FieldDefinitionDto(
    Guid Id, string Code, string Label, FieldType FieldType, bool IsRequired, bool IsActive,
    int DisplayOrder, string? OptionsJson, Guid? LookupFormDefinitionId, string? ValidationRulesJson);

public record FormVersionDto(Guid Id, int VersionNumber, FormStatus Status, DateTime? PublishedAtUtc,
    IReadOnlyList<FieldDefinitionDto> Fields);

public record FormDefinitionDto(
    Guid Id, string Code, string Name, string? Description, string ModuleName,
    FormStatus Status, string? TableName, FormVersionDto? DraftVersion, FormVersionDto? PublishedVersion);

public record NewFieldDto(
    string Code, string Label, FieldType FieldType, bool IsRequired,
    string? OptionsJson, Guid? LookupFormDefinitionId, string? ValidationRulesJson);
