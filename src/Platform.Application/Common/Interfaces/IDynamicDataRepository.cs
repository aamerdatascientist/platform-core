using Platform.Domain.Forms;

namespace Platform.Application.Common.Interfaces;

public record DynamicRow(Guid Id, IReadOnlyDictionary<string, object?> Values);

public record PagedResult<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize);

/// <summary>
/// A single column-equals-value predicate for QueryAsync. Value must already be the CLR
/// type the physical column expects (e.g. Guid for a Lookup field) - callers resolve that
/// from the FieldDefinition's FieldType before constructing this, same as InsertAsync's
/// ConvertFieldValue does for writes.
/// </summary>
public record DynamicRowFilter(string FieldCode, object Value);

/// <summary>
/// A submitted row with who sent it and when (redesign phase 7: My entries, Records).
/// </summary>
public record RecordRow(Guid Id, DateTime CreatedAtUtc, Guid CreatedByUserId, IReadOnlyDictionary<string, object?> Values);

/// <summary>
/// What SearchAsync filters by - all optional, all ANDed. Field codes are resolved from real
/// FieldDefinitions by the caller and still go through the identifier check; every value is a
/// parameter. Text matches any text/dropdown column (case-insensitive, LIKE wildcards escaped).
/// The date range applies to a DateTime field (DateFieldCode) or, without one, to CreatedAtUtc;
/// From is inclusive, To exclusive. Before is a (CreatedAtUtc, Id) cursor for newest-first paging.
/// </summary>
public record RecordSearch(
    int Page = 1,
    int PageSize = 25,
    IReadOnlyList<DynamicRowFilter>? Matches = null,
    string? Text = null,
    string? DateFieldCode = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    Guid? CreatedByUserId = null,
    (DateTime CreatedAtUtc, Guid Id)? Before = null,
    bool CountTotal = true);

/// <summary>One submission: who created the row and when (UTC).</summary>
/// <summary>Who sent a row, when, and its id (the tracking grid opens that entry - redesign 23).</summary>
/// <summary>The blocks of one repeatable section, to write with their parent row.</summary>
public record SectionRows(string ChildTableName, IReadOnlyCollection<FieldDefinition> Fields, IReadOnlyList<IReadOnlyDictionary<string, object?>> Blocks);

/// <summary>One stored block of a repeatable section.</summary>
public record BlockRow(Guid Id, Guid ParentId, int BlockIndex, IReadOnlyDictionary<string, object?> Values);

/// <summary>
/// Options for a write that must be atomic with checks made against other rows: the write
/// takes a Postgres advisory lock on <paramref name="LockKey"/> for its transaction and runs
/// <paramref name="WhileLocked"/> before writing, so two submissions on the same key are
/// checked and written one after the other (2026-11-10: stage eligibility rules).
/// </summary>
public record WriteGuard(string? LockKey, Func<CancellationToken, Task>? WhileLocked);

public record SubmissionStamp(Guid UserId, DateTime CreatedAtUtc, Guid? RecordId = null);

/// <summary>
/// CRUD against a dynamically generated table. Every method takes the FormVersion's
/// active FieldDefinitions explicitly rather than re-deriving them, so the repository
/// never has to guess which columns are safe to touch - it only ever writes to columns
/// backed by a known FieldDefinition.Code, which is the injection safety boundary.
/// </summary>
public interface IDynamicDataRepository
{
    Task<Guid> InsertAsync(
        string tableName,
        IReadOnlyCollection<FieldDefinition> activeFields,
        IReadOnlyDictionary<string, object?> values,
        Guid submittedByUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Overwrites every active, non-Attachment field of one non-deleted row with
    /// <paramref name="values"/> (a field missing from the dictionary becomes null - the same
    /// full-record shape InsertAsync takes) and stamps ModifiedAtUtc/ModifiedByUserId. False
    /// when no such row exists (never existed, or deleted).
    /// </summary>
    /// <summary>Inserts the parent row and every section's blocks in one transaction. Fields
    /// in a section never go to the parent table, whatever <paramref name="activeFields"/> holds.</summary>
    Task<Guid> InsertWithSectionsAsync(
        string tableName,
        IReadOnlyCollection<FieldDefinition> activeFields,
        IReadOnlyDictionary<string, object?> values,
        IReadOnlyList<SectionRows> sections,
        Guid submittedByUserId,
        WriteGuard? guard = null,
        CancellationToken cancellationToken = default);

    /// <summary>Updates the parent row and replaces every given section's blocks (old blocks
    /// are soft-deleted, the new ones inserted) in one transaction.</summary>
    Task<bool> UpdateWithSectionsAsync(
        string tableName,
        IReadOnlyCollection<FieldDefinition> activeFields,
        Guid id,
        IReadOnlyDictionary<string, object?> values,
        IReadOnlyList<SectionRows> sections,
        Guid modifiedByUserId,
        WriteGuard? guard = null,
        CancellationToken cancellationToken = default);

    /// <summary>The live blocks of the given parents, ordered by parent then block index.</summary>
    Task<IReadOnlyList<BlockRow>> GetBlocksAsync(
        string childTableName,
        IReadOnlyCollection<FieldDefinition> sectionFields,
        IReadOnlyCollection<Guid> parentIds,
        CancellationToken cancellationToken = default);

    /// <summary>Every live block of a section with its parent's id - for calculations that read
    /// a whole form (parent rows come from the usual queries).</summary>
    Task<IReadOnlyList<BlockRow>> GetAllBlocksAsync(
        string childTableName,
        string parentTableName,
        IReadOnlyCollection<FieldDefinition> sectionFields,
        CancellationToken cancellationToken = default);

    Task<bool> UpdateAsync(
        string tableName,
        IReadOnlyCollection<FieldDefinition> activeFields,
        Guid id,
        IReadOnlyDictionary<string, object?> values,
        Guid modifiedByUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Soft delete: sets IsDeleted (so every read, count and dashboard skips the row) and
    /// stamps ModifiedAtUtc/ModifiedByUserId. The row and its values stay in the table. False
    /// when no such non-deleted row exists.
    /// </summary>
    Task<bool> SoftDeleteAsync(
        string tableName, Guid id, Guid deletedByUserId, CancellationToken cancellationToken = default);

    Task<DynamicRow?> GetByIdAsync(
        string tableName,
        IReadOnlyCollection<FieldDefinition> activeFields,
        Guid id,
        CancellationToken cancellationToken = default);

    /// <summary>Filtered, newest-first rows with sender and time (see RecordSearch).</summary>
    Task<PagedResult<RecordRow>> SearchAsync(
        string tableName,
        IReadOnlyCollection<FieldDefinition> activeFields,
        RecordSearch search,
        CancellationToken cancellationToken = default);

    /// <summary>One row with sender and time, or null (missing or deleted).</summary>
    Task<RecordRow?> GetRecordAsync(
        string tableName,
        IReadOnlyCollection<FieldDefinition> activeFields,
        Guid id,
        CancellationToken cancellationToken = default);

    Task<PagedResult<DynamicRow>> QueryAsync(
        string tableName,
        IReadOnlyCollection<FieldDefinition> activeFields,
        int page,
        int pageSize,
        DynamicRowFilter? filter = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Same COUNT(1)/IsDeleted=false predicate QueryAsync already uses for its own
    /// TotalCount - split out standalone for aggregate queries (e.g. submissions-per-form)
    /// that need a row count without paging through the rows themselves.
    /// </summary>
    Task<int> CountAsync(string tableName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Distinct, non-null, non-blank submitted values of one column, across every
    /// non-deleted row - backs a Dropdown field's dynamic options source (see
    /// FieldDefinition.DynamicOptionsSourceFormDefinitionId/Code and
    /// GetFieldDynamicOptionsQuery): e.g. Outflow's "material" Dropdown reads this against
    /// Inflow's table/"material" column so it always offers exactly what's actually been
    /// received. Always a text column in practice (dynamic options only ever source from
    /// another Dropdown/ShortText field), so this returns plain strings, not FieldType-typed
    /// values the way QueryAsync's rows do.
    /// </summary>
    Task<IReadOnlyList<string>> GetDistinctColumnValuesAsync(
        string tableName, string columnCode, CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether another non-deleted row already holds <paramref name="text"/> in a text column,
    /// compared trimmed and case-insensitively ("az-z1 " matches "AZ-Z1"). With
    /// <paramref name="scopeColumnCode"/> only rows whose value in that column equals
    /// <paramref name="scopeValue"/> count (e.g. zone codes unique within one project; a null
    /// scope value matches rows where it is null). <paramref name="excludeId"/> leaves out the
    /// record being edited. Backs the "unique" field rule (SubmissionDataChecks).
    /// </summary>
    /// <summary>
    /// Who submitted a row of this table and when (CreatedByUserId / CreatedAtUtc, UTC), for
    /// non-deleted rows by <paramref name="userIds"/> created in [fromUtc, toUtc). Backs the
    /// form assignments' "filled or not" - read from the data itself, never stored twice.
    /// </summary>
    Task<IReadOnlyList<SubmissionStamp>> GetSubmissionStampsAsync(
        string tableName, IReadOnlyCollection<Guid> userIds, DateTime fromUtc, DateTime toUtc,
        CancellationToken cancellationToken = default);

    Task<bool> AnyOtherWithTextAsync(
        string tableName, string columnCode, string text,
        string? scopeColumnCode, object? scopeValue, Guid? excludeId,
        CancellationToken cancellationToken = default);
}
