using Platform.Application.Common.Interfaces;
using Platform.Domain.Forms;

namespace Platform.Infrastructure.IntegrationTests.Analytics;

/// <summary>
/// Hand-written test double rather than a mocking library - the project has none yet, and
/// GetSubmissionCountByModuleQueryHandler only ever calls CountAsync, so a real mock adds a
/// new dependency for one method. Table names not registered via <see cref="Counts"/>
/// throw, so a test that queries an unexpected table fails loudly instead of silently
/// returning 0 and passing for the wrong reason.
/// </summary>
public class FakeDynamicDataRepository : IDynamicDataRepository
{
    public Dictionary<string, int> Counts { get; } = new();

    public Task<int> CountAsync(string tableName, CancellationToken cancellationToken = default)
    {
        if (!Counts.TryGetValue(tableName, out var count))
            throw new InvalidOperationException($"Test bug: no fake count registered for table '{tableName}'.");

        return Task.FromResult(count);
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

    public Task<PagedResult<DynamicRow>> QueryAsync(
        string tableName, IReadOnlyCollection<FieldDefinition> activeFields, int page, int pageSize,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Not used by the analytics query handler tests.");
}
