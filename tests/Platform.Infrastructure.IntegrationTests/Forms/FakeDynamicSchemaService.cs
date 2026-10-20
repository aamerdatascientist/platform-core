using Platform.Application.Common.Interfaces;
using Platform.Domain.Forms;
using Platform.Domain.Forms.Enums;

namespace Platform.Infrastructure.IntegrationTests.Forms;

/// <summary>
/// Records the DDL a command asked for, without a database. Only what the field-rule and
/// archive tests use is implemented; anything else throws, so an unexpected DDL call fails
/// the test loudly.
/// </summary>
public class FakeDynamicSchemaService : IDynamicSchemaService
{
    public List<(string Table, string Column)> DroppedNotNull { get; } = new();
    public int ViewRefreshes { get; private set; }

    public Task DropNotNullAsync(string tableName, string columnCode, CancellationToken cancellationToken = default)
    {
        DroppedNotNull.Add((tableName, columnCode));
        return Task.CompletedTask;
    }

    public Task RefreshReportingViewAsync(
        FormDefinition formDefinition, FormVersion version, IReadOnlyDictionary<Guid, FormDefinition>? lookupTargets = null,
        CancellationToken cancellationToken = default)
    {
        ViewRefreshes++;
        return Task.CompletedTask;
    }

    public Task<string> EnsureTableForPublishedVersionAsync(FormDefinition formDefinition, FormVersion version, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<bool> ColumnExistsAsync(string tableName, string columnCode, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task AddColumnForFieldAsync(string tableName, FieldDefinition field, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task RenameColumnAsync(string tableName, string fromCode, string toCode, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<Guid>> FindRowsFailingTypeChangeAsync(string tableName, FieldDefinition field, FieldType newType, int maxRows = 10, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task ChangeColumnTypeAsync(FormDefinition formDefinition, FormVersion version, FieldDefinition field, FieldType newType, IReadOnlyDictionary<Guid, FormDefinition>? lookupTargets = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task DropColumnAsync(FormDefinition formDefinition, FormVersion version, string columnCode, IReadOnlyDictionary<Guid, FormDefinition>? lookupTargets = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}
