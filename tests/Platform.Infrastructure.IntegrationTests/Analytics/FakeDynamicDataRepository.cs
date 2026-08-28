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

    /// <summary>Full result set per table, for QueryAsync - the Executive Overview handlers
    /// use this to resolve master-data forms (Projects/Materials/Locations) and, for
    /// Projects specifically, as their own primary data source.</summary>
    public Dictionary<string, List<DynamicRow>> Rows { get; } = new();

    public Task<int> CountAsync(string tableName, CancellationToken cancellationToken = default)
    {
        if (!Counts.TryGetValue(tableName, out var count))
            throw new InvalidOperationException($"Test bug: no fake count registered for table '{tableName}'.");

        return Task.FromResult(count);
    }

    public Task<PagedResult<DynamicRow>> QueryAsync(
        string tableName, IReadOnlyCollection<FieldDefinition> activeFields, int page, int pageSize,
        CancellationToken cancellationToken = default)
    {
        if (!Rows.TryGetValue(tableName, out var rows))
            throw new InvalidOperationException($"Test bug: no fake rows registered for table '{tableName}'.");

        // No real paging - every Executive Overview caller fetches page 1 of a generous
        // page size expecting the whole (small) master-data set back, same as production.
        return Task.FromResult(new PagedResult<DynamicRow>(rows, rows.Count, page, pageSize));
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
}
