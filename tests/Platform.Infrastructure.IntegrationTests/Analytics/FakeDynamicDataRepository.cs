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

    /// <summary>Set to make the next InsertAsync fail (the offline-queue tests).</summary>
    public bool FailNextInsert { get; set; }

    /// <summary>Blocks of repeatable sections, per child table.</summary>
    public Dictionary<string, List<BlockRow>> Blocks { get; } = new();

    /// <summary>The guard of the last section-aware write (lock key), for hook tests.</summary>
    public List<string?> LockKeys { get; } = new();

    public async Task<Guid> InsertWithSectionsAsync(
        string tableName, IReadOnlyCollection<FieldDefinition> activeFields, IReadOnlyDictionary<string, object?> values,
        IReadOnlyList<SectionRows> sections, Guid submittedByUserId, WriteGuard? guard = null,
        CancellationToken cancellationToken = default)
    {
        LockKeys.Add(guard?.LockKey);
        if (guard?.WhileLocked is not null) await guard.WhileLocked(cancellationToken);
        var id = await InsertAsync(tableName, activeFields.Where(f => f.SectionId is null).ToList(), values, submittedByUserId, cancellationToken);
        AddBlocks(sections, id);
        return id;
    }

    public async Task<bool> UpdateWithSectionsAsync(
        string tableName, IReadOnlyCollection<FieldDefinition> activeFields, Guid id, IReadOnlyDictionary<string, object?> values,
        IReadOnlyList<SectionRows> sections, Guid modifiedByUserId, WriteGuard? guard = null,
        CancellationToken cancellationToken = default)
    {
        LockKeys.Add(guard?.LockKey);
        if (guard?.WhileLocked is not null) await guard.WhileLocked(cancellationToken);
        var ok = await UpdateAsync(tableName, activeFields.Where(f => f.SectionId is null).ToList(), id, values, modifiedByUserId, cancellationToken);
        if (!ok) return false;
        foreach (var section in sections)
            if (Blocks.TryGetValue(section.ChildTableName, out var list)) list.RemoveAll(b => b.ParentId == id);
        AddBlocks(sections, id);
        return true;
    }

    private void AddBlocks(IReadOnlyList<SectionRows> sections, Guid parentId)
    {
        foreach (var section in sections)
        {
            if (!Blocks.TryGetValue(section.ChildTableName, out var list)) Blocks[section.ChildTableName] = list = new();
            for (var i = 0; i < section.Blocks.Count; i++)
                list.Add(new BlockRow(Guid.NewGuid(), parentId, i, section.Blocks[i]));
        }
    }

    public Task<IReadOnlyList<BlockRow>> GetBlocksAsync(
        string childTableName, IReadOnlyCollection<FieldDefinition> sectionFields, IReadOnlyCollection<Guid> parentIds,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<BlockRow>>((Blocks.GetValueOrDefault(childTableName) ?? new()).Where(b => parentIds.Contains(b.ParentId)).ToList());

    public Task<IReadOnlyList<BlockRow>> GetAllBlocksAsync(
        string childTableName, string parentTableName, IReadOnlyCollection<FieldDefinition> sectionFields,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<BlockRow>>(Blocks.GetValueOrDefault(childTableName) ?? new());

    public Task<Guid> InsertAsync(
        string tableName, IReadOnlyCollection<FieldDefinition> activeFields,
        IReadOnlyDictionary<string, object?> values, Guid submittedByUserId,
        CancellationToken cancellationToken = default)
    {
        if (FailNextInsert)
        {
            FailNextInsert = false;
            throw new InvalidOperationException("insert failed (test)");
        }
        var row = new RecordRow(Guid.NewGuid(), DateTime.UtcNow, submittedByUserId, values.ToDictionary(kv => kv.Key, kv => kv.Value));
        if (!Records.TryGetValue(tableName, out var rows)) Records[tableName] = rows = new List<RecordRow>();
        rows.Add(row);
        return Task.FromResult(row.Id);
    }

    /// <summary>Rows with sender and time, for SearchAsync/GetRecordAsync (My entries, Records).</summary>
    public Dictionary<string, List<RecordRow>> Records { get; } = new();

    /// <summary>The last search per table, so tests can check what the handler asked for.</summary>
    public Dictionary<string, RecordSearch> LastSearch { get; } = new();

    public Task<PagedResult<RecordRow>> SearchAsync(
        string tableName, IReadOnlyCollection<FieldDefinition> activeFields, RecordSearch search,
        CancellationToken cancellationToken = default)
    {
        LastSearch[tableName] = search;
        var rows = Records.GetValueOrDefault(tableName) ?? new List<RecordRow>();
        IEnumerable<RecordRow> q = rows;
        foreach (var f in search.Matches ?? Array.Empty<DynamicRowFilter>())
            q = q.Where(r => Equals(r.Values.GetValueOrDefault(f.FieldCode), f.Value));
        if (search.CreatedByUserId is { } u) q = q.Where(r => r.CreatedByUserId == u);
        if (search.Before is { } b) q = q.Where(r => r.CreatedAtUtc < b.CreatedAtUtc || (r.CreatedAtUtc == b.CreatedAtUtc && r.Id.CompareTo(b.Id) < 0));
        if (!string.IsNullOrWhiteSpace(search.Text))
            q = q.Where(r => r.Values.Values.OfType<string>().Any(v => v.Contains(search.Text, StringComparison.OrdinalIgnoreCase)));
        var all = q.OrderByDescending(r => r.CreatedAtUtc).ThenByDescending(r => r.Id).ToList();
        var page = all.Skip((search.Page - 1) * search.PageSize).Take(search.PageSize).ToList();
        return Task.FromResult(new PagedResult<RecordRow>(page, all.Count, search.Page, search.PageSize));
    }

    public Task<RecordRow?> GetRecordAsync(
        string tableName, IReadOnlyCollection<FieldDefinition> activeFields, Guid id,
        CancellationToken cancellationToken = default) =>
        Task.FromResult((Records.GetValueOrDefault(tableName) ?? new List<RecordRow>()).SingleOrDefault(r => r.Id == id));

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

    public Task<IReadOnlyList<SubmissionStamp>> GetSubmissionStampsAsync(
        string tableName, IReadOnlyCollection<Guid> userIds, DateTime fromUtc, DateTime toUtc,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<SubmissionStamp>>((Records.GetValueOrDefault(tableName) ?? new List<RecordRow>())
            .Where(r => userIds.Contains(r.CreatedByUserId) && r.CreatedAtUtc >= fromUtc && r.CreatedAtUtc < toUtc)
            .Select(r => new SubmissionStamp(r.CreatedByUserId, r.CreatedAtUtc, r.Id))
            .ToList());

    public Task<bool> AnyOtherWithTextAsync(
        string tableName, string columnCode, string text,
        string? scopeColumnCode, object? scopeValue, Guid? excludeId,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Not used by the analytics query handler tests.");
}
