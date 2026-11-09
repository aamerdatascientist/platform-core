namespace Platform.Domain.Forms.Enums;

/// <summary>
/// Every value here except Attachment maps to a real physical column on the
/// generated dynamic table - see Infrastructure/Persistence/DynamicSchema/SqlTypeMapper.
/// Attachment fields never get a physical column: they are resolved through the
/// (future) File Management module keyed by SubmissionId + FieldDefinitionId, keeping
/// binary/file data out of the structured store entirely, per platform requirements.
/// </summary>
public enum FieldType
{
    ShortText = 0,
    LongText = 1,
    Number = 2,
    Decimal = 3,
    Boolean = 4,
    DateTime = 5,
    Dropdown = 6,
    Lookup = 7,
    Attachment = 8,
    /// <summary>Several of a fixed (or provider-given) option list; stored as text[] (added 2026-11-10).</summary>
    MultiSelect = 9,
    /// <summary>Several rows of another form; stored as uuid[] (added 2026-11-10).</summary>
    MultiLookup = 10
}

public enum FormStatus
{
    Draft = 0,
    Published = 1,
    Retired = 2
}
