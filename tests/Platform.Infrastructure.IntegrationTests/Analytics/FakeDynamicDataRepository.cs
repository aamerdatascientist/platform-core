using Platform.Application.Common.Interfaces;
using Platform.Domain.Forms;

namespace Platform.Infrastructure.IntegrationTests.Analytics;

/// <summary>
/// Hand-written test double rather than a mocking library - the project has none yet.
/// Table names not registered via <see cref="Counts"/>/<see cref="Rows"/> throw, so a test
/// that queries an unexpected table fails loudly instead of silently returning empty and
/// passing for the wrong reason.
/// </summary>
public class FakeDynamicDataRepository : IDynamicDataRepository
{
    public Dictionary<string, int> Counts { get; } = new();

    /// <summary>Full result set per table, for QueryAsync (which pages and filters it the
    /// way the real repository does).</summary>
    public Dictionary<string, List<DynamicRow>> Rows { get; } = new();

    public Task<int> CountAsync(string tableName, CancellationToken cancellationToken = default)
    {
        if (!Counts.TryGetValue(tableName, out var count))
            throw new InvalidOperationException($"Test bug: no fake count registered for table '{tableName}'.");

        return Task.FromResult(count);
    }

    public Task<PagedResult<DynamicRow>> QueryAsync(
        string tableName, IReadOnlyCollection<FieldDefinition> activeFields, int page, int pageSize,
        DynamicRowFilter? filter = null, CancellationToken cancellationToken = default)
    {
        if (!Rows.TryGetValue(tableName, out var rows))
            throw new InvalidOperationException($"Test bug: no fake rows registered for table '{tableName}'.");

        // Equality filter, same predicate as the real repository's WHERE "{code}" = @value.
        var matching = filter is null
            ? rows
            : rows.Where(r => Equals(r.Values.GetValueOrDefault(filter.FieldCode), filter.Value)).ToList();

        // Real paging, so callers that loop over pages are exercised the way production runs them.
        var items = matching.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        return Task.FromResult(new PagedResult<DynamicRow>(items, matching.Count, page, pageSize));
    }

    public Task<Guid> InsertAsync(
        string tableName, IReadOnlyCollection<FieldDefinition> activeFields,
        IReadOnlyDictionary<string, object?> values, Guid submittedByUserId,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Not used by the analytics query handler tests.");

    public Task<DynamicRow?> GetByIdAsync(
        string tableName, IReadOnlyCollection<FieldDefinition> activeFields, Guid id,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Not used by the analytics query handler tests.");

    /// <summary>Who changed or deleted which row, for the record edit/delete tests.</summary>
    public List<(string Table, Guid Id, Guid UserId)> Modifications { get; } = new();

    public Task<bool> UpdateAsync(
        string tableName, IReadOnlyCollection<FieldDefinition> activeFields, Guid id,
        IReadOnlyDictionary<string, object?> values, Guid modifiedByUserId,
        CancellationToken cancellationToken = default)
    {
        if (!Rows.TryGetValue(tableName, out var rows))
            throw new InvalidOperationException($"Test bug: no fake rows registered for table '{tableName}'.");
        var index = rows.FindIndex(r => r.Id == id);
        if (index < 0) return Task.FromResult(false);

        // Same full-record overwrite as the real UPDATE: every writable active field is set,
        // and one missing from the values becomes null.
        var updated = activeFields
            .Where(f => f.FieldType != Platform.Domain.Forms.Enums.FieldType.Attachment)
            .ToDictionary(f => f.Code, f => values.GetValueOrDefault(f.Code));
        rows[index] = new DynamicRow(id, updated);
        Modifications.Add((tableName, id, modifiedByUserId));
        return Task.FromResult(true);
    }

    public Task<bool> SoftDeleteAsync(
        string tableName, Guid id, Guid deletedByUserId, CancellationToken cancellationToken = default)
    {
        if (!Rows.TryGetValue(tableName, out var rows))
            throw new InvalidOperationException($"Test bug: no fake rows registered for table '{tableName}'.");
        var removed = rows.RemoveAll(r => r.Id == id) > 0;
        if (removed) Modifications.Add((tableName, id, deletedByUserId));
        return Task.FromResult(removed);
    }

    public Task<IReadOnlyList<string>> GetDistinctColumnValuesAsync(
        string tableName, string columnCode, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Not used by the analytics query handler tests.");

    public Task<bool> AnyOtherWithTextAsync(
        string tableName, string columnCode, string text,
        string? scopeColumnCode, object? scopeValue, Guid? excludeId,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Not used by the analytics query handler tests.");
}
