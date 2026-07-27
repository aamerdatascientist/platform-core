using System.Text;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Platform.Application.Common.Interfaces;
using Platform.Domain.Forms;
using Platform.Domain.Forms.Enums;

namespace Platform.Infrastructure.Persistence.DynamicSchema;

public class DynamicSchemaService : IDynamicSchemaService
{
    private readonly string _connectionString;
    private readonly ILogger<DynamicSchemaService> _logger;

    public DynamicSchemaService(IConfiguration configuration, ILogger<DynamicSchemaService> logger)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection is not configured.");
        _logger = logger;
    }

    public async Task<string> EnsureTableForPublishedVersionAsync(
        FormDefinition formDefinition, FormVersion version, CancellationToken cancellationToken = default)
    {
        var tableName = formDefinition.TableName ?? $"Data_{SqlTypeMapper.ToPascalCase(formDefinition.Code)}";
        SqlTypeMapper.AssertSafeIdentifier(tableName);

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var activeFields = version.Fields.Where(f => f.IsActive && f.FieldType != FieldType.Attachment).ToList();

        if (!await TableExistsAsync(connection, tableName, cancellationToken))
        {
            await CreateTableAsync(connection, tableName, activeFields, cancellationToken);
            _logger.LogInformation("Created dynamic table {TableName} for form {FormCode}", tableName, formDefinition.Code);
        }
        else
        {
            foreach (var field in activeFields)
            {
                if (!await ColumnExistsAsync(tableName, field.Code, connection, cancellationToken))
                {
                    await AddColumnAsync(connection, tableName, field, cancellationToken);
                    _logger.LogInformation(
                        "Added column {ColumnName} to {TableName} for form {FormCode}",
                        field.Code, tableName, formDefinition.Code);
                }
            }
        }

        return tableName;
    }

    public async Task RefreshReportingViewAsync(
        FormDefinition formDefinition, FormVersion version,
        IReadOnlyDictionary<Guid, FormDefinition>? lookupTargets = null,
        CancellationToken cancellationToken = default)
    {
        var tableName = formDefinition.TableName
            ?? throw new InvalidOperationException("Cannot build a reporting view before the table has been created.");
        var viewName = $"Report_{SqlTypeMapper.ToPascalCase(formDefinition.Code)}";
        SqlTypeMapper.AssertSafeIdentifier(viewName);
        SqlTypeMapper.AssertSafeIdentifier(tableName);

        var activeFields = version.Fields.Where(f => f.IsActive && f.FieldType != FieldType.Attachment).ToList();

        var selectColumns = new StringBuilder();
        var joins = new StringBuilder();
        selectColumns.Append("        d.[Id] AS [Record Id],\n");
        selectColumns.Append("        d.[CreatedAtUtc] AS [Submitted At],\n");
        selectColumns.Append("        creator.[DisplayName] AS [Submitted By]");

        foreach (var field in activeFields)
        {
            SqlTypeMapper.AssertSafeIdentifier(field.Code);
            // Labels are free text, not identifiers - bracket-quoted aliases don't need to be
            // valid identifiers, but embedded ']' must still be escaped to close the quote safely.
            var escapedLabel = field.Label.Replace("]", "]]");

            var displaySource = field.FieldType == FieldType.Lookup
                ? ResolveLookupDisplaySource(field, lookupTargets)
                : null;

            if (displaySource is null)
            {
                selectColumns.Append($",\n        d.[{field.Code}] AS [{escapedLabel}]");
            }
            else
            {
                var (targetTable, displayColumn) = displaySource.Value;
                var joinAlias = $"lkp_{field.Code}";
                SqlTypeMapper.AssertSafeIdentifier(joinAlias);

                joins.Append($"\n        LEFT JOIN [{targetTable}] [{joinAlias}] ON [{joinAlias}].[Id] = d.[{field.Code}]");
                selectColumns.Append($",\n        [{joinAlias}].[{displayColumn}] AS [{escapedLabel}]");
            }
        }

        var sql = $"""
            CREATE OR ALTER VIEW [{viewName}] AS
            SELECT
            {selectColumns}
            FROM [{tableName}] d
            LEFT JOIN [Users] creator ON creator.[Id] = d.[CreatedByUserId]{joins}
            WHERE d.[IsDeleted] = 0;
            """;

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);

        _logger.LogInformation("Refreshed reporting view {ViewName} for form {FormCode}", viewName, formDefinition.Code);
    }

    /// <summary>
    /// A Lookup field only gets resolved to a readable value if its target form is present in
    /// <paramref name="lookupTargets"/>, is published (has a TableName), and has at least one
    /// active ShortText field to show - falling back to the raw referenced Id otherwise is
    /// deliberate: a half-resolved join is worse than an honest raw value.
    /// </summary>
    private static (string TargetTable, string DisplayColumn)? ResolveLookupDisplaySource(
        FieldDefinition field, IReadOnlyDictionary<Guid, FormDefinition>? lookupTargets)
    {
        if (field.LookupFormDefinitionId is not { } targetId) return null;
        if (lookupTargets is null || !lookupTargets.TryGetValue(targetId, out var target)) return null;
        if (target.TableName is not { } targetTable) return null;

        var displayField = target.GetPublishedVersion()?.Fields
            .Where(f => f.IsActive && f.FieldType == FieldType.ShortText)
            .OrderBy(f => f.DisplayOrder)
            .FirstOrDefault();

        return displayField is null ? null : (targetTable, displayField.Code);
    }

    public async Task<bool> ColumnExistsAsync(string tableName, string columnCode, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return await ColumnExistsAsync(tableName, columnCode, connection, cancellationToken);
    }

    private static async Task<bool> TableExistsAsync(SqlConnection connection, string tableName, CancellationToken ct)
    {
        const string sql = """
            SELECT CAST(COUNT(1) AS bit) FROM INFORMATION_SCHEMA.TABLES
            WHERE TABLE_NAME = @tableName AND TABLE_TYPE = 'BASE TABLE';
            """;
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@tableName", tableName);
        return (bool)(await command.ExecuteScalarAsync(ct))!;
    }

    private static async Task<bool> ColumnExistsAsync(
        string tableName, string columnCode, SqlConnection connection, CancellationToken ct)
    {
        const string sql = """
            SELECT CAST(COUNT(1) AS bit) FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_NAME = @tableName AND COLUMN_NAME = @columnName;
            """;
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@tableName", tableName);
        command.Parameters.AddWithValue("@columnName", columnCode);
        return (bool)(await command.ExecuteScalarAsync(ct))!;
    }

    private static async Task CreateTableAsync(
        SqlConnection connection, string tableName, IReadOnlyCollection<FieldDefinition> fields, CancellationToken ct)
    {
        var columns = new StringBuilder();
        foreach (var field in fields)
        {
            SqlTypeMapper.AssertSafeIdentifier(field.Code);
            var nullability = field.IsRequired ? "NOT NULL" : "NULL";
            columns.Append($",\n    [{field.Code}] {SqlTypeMapper.ToSqlColumnType(field.FieldType)} {nullability}");
        }

        var sql = $"""
            CREATE TABLE [{tableName}] (
                [Id] uniqueidentifier NOT NULL CONSTRAINT [PK_{tableName}] PRIMARY KEY DEFAULT NEWID(),
                [FormVersionId] uniqueidentifier NOT NULL,
                [CreatedAtUtc] datetime2 NOT NULL,
                [CreatedByUserId] uniqueidentifier NOT NULL,
                [ModifiedAtUtc] datetime2 NULL,
                [ModifiedByUserId] uniqueidentifier NULL,
                [IsDeleted] bit NOT NULL DEFAULT 0,
                [Extensions] nvarchar(max) NULL{columns}
            );
            """;

        await using var command = new SqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task AddColumnAsync(
        SqlConnection connection, string tableName, FieldDefinition field, CancellationToken ct)
    {
        SqlTypeMapper.AssertSafeIdentifier(field.Code);

        // New columns on an already-live table are always nullable, even if the field is
        // marked required going forward - existing rows have no value to backfill, and a
        // NOT NULL ALTER would fail outright. Required-ness is enforced at submission time
        // in the Application layer instead, see SubmitFormDataCommandHandler.
        var sql = $"ALTER TABLE [{tableName}] ADD [{field.Code}] {SqlTypeMapper.ToSqlColumnType(field.FieldType)} NULL;";

        await using var command = new SqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(ct);
    }
}
