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

    Task<DynamicRow?> GetByIdAsync(
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
}
