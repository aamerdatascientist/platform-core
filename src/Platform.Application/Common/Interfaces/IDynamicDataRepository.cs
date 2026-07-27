using Platform.Domain.Forms;

namespace Platform.Application.Common.Interfaces;

public record DynamicRow(Guid Id, IReadOnlyDictionary<string, object?> Values);

public record PagedResult<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize);

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
        CancellationToken cancellationToken = default);
}
