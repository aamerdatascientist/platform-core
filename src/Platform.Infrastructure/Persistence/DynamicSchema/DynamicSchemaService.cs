using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;
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
        _connectionString = configuration.GetConnectionString("PostgresConnection")
            ?? throw new InvalidOperationException("ConnectionStrings:PostgresConnection is not configured.");
        _logger = logger;
    }

    public async Task<string> EnsureTableForPublishedVersionAsync(
        FormDefinition formDefinition, FormVersion version, CancellationToken cancellationToken = default)
    {
        var tableName = formDefinition.TableName ?? $"Data_{SqlTypeMapper.ToPascalCase(formDefinition.Code)}";
        SqlTypeMapper.AssertSafePostgresIdentifier(tableName);

        var activeFields = version.Fields.Where(f => f.IsActive && f.FieldType != FieldType.Attachment).ToList();

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        // A transaction-scoped advisory lock, keyed on the table name (stable and unique
        // per form once assigned), so two concurrent first-publish requests for the same
        // form can't both observe "table doesn't exist" and race to CREATE TABLE - the
        // loser would otherwise fail with a raw duplicate-table error instead of the clean,
        // idempotent outcome a second publish is supposed to have. Transaction-scoped means
        // it releases automatically on commit or rollback, no separate unlock call needed.
        // Wrapping the whole create-or-alter decision (not just the CREATE branch) is what
        // makes the re-check after acquiring the lock actually meaningful - checking
        // existence before taking the lock would still leave the same race between the
        // check and the lock acquisition.
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (var lockCommand = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtext(@lockKey));", connection, transaction))
        {
            lockCommand.Parameters.AddWithValue("@lockKey", tableName);
            await lockCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        if (!await TableExistsAsync(connection, transaction, tableName, cancellationToken))
        {
            await CreateTableAsync(connection, transaction, tableName, activeFields, cancellationToken);
            _logger.LogInformation("Created dynamic table {TableName} for form {FormCode}", tableName, formDefinition.Code);
        }
        else
        {
            foreach (var field in activeFields)
            {
                if (!await ColumnExistsAsync(connection, transaction, tableName, field.Code, cancellationToken))
                {
                    await AddColumnAsync(connection, transaction, tableName, field, cancellationToken);
                    _logger.LogInformation(
                        "Added column {ColumnName} to {TableName} for form {FormCode}",
                        field.Code, tableName, formDefinition.Code);
                }
            }
        }

        await transaction.CommitAsync(cancellationToken);
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
        SqlTypeMapper.AssertSafePostgresIdentifier(viewName);
        SqlTypeMapper.AssertSafePostgresIdentifier(tableName);

        var activeFields = version.Fields.Where(f => f.IsActive && f.FieldType != FieldType.Attachment).ToList();

        var selectColumns = new StringBuilder();
        var joins = new StringBuilder();
        selectColumns.Append("        d.\"Id\" AS \"Record Id\",\n");
        selectColumns.Append("        d.\"CreatedAtUtc\" AS \"Submitted At\",\n");
        selectColumns.Append("        creator.\"DisplayName\" AS \"Submitted By\"");

        // Kept as case-insensitive disambiguation even though Postgres's double-quoted
        // identifiers are themselves case-sensitive - unlike SQL Server's default collation,
        // which is what originally made this necessary there. Two field labels differing
        // only by case wouldn't strictly collide on Postgres, but treating them as the same
        // name here is still the safer, less confusing choice, and keeps this logic
        // identical across both dialects rather than diverging behavior on a port.
        var usedColumnNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Record Id", "Submitted At", "Submitted By"
        };

        foreach (var field in activeFields)
        {
            SqlTypeMapper.AssertSafePostgresIdentifier(field.Code);

            var displayLabel = usedColumnNames.Add(field.Label) ? field.Label : $"{field.Label} ({field.Code})";
            usedColumnNames.Add(displayLabel);

            // Labels are free text, not identifiers - double-quoted aliases don't need to be
            // valid identifiers, but an embedded '"' must still be escaped to close the quote
            // safely (Postgres's quoted-identifier escape is "" , not SQL Server's ]] ).
            var escapedLabel = displayLabel.Replace("\"", "\"\"");

            var displaySource = field.FieldType == FieldType.Lookup
                ? ResolveLookupDisplaySource(field, lookupTargets)
                : null;

            if (displaySource is null)
            {
                selectColumns.Append($",\n        d.\"{field.Code}\" AS \"{escapedLabel}\"");
            }
            else
            {
                var (targetTable, displayColumn) = displaySource.Value;
                var joinAlias = BuildSafeJoinAlias(field.Code);
                SqlTypeMapper.AssertSafePostgresIdentifier(joinAlias);

                joins.Append($"\n        LEFT JOIN \"{targetTable}\" \"{joinAlias}\" ON \"{joinAlias}\".\"Id\" = d.\"{field.Code}\"");
                selectColumns.Append($",\n        \"{joinAlias}\".\"{displayColumn}\" AS \"{escapedLabel}\"");
            }
        }

        var selectSql = $"""
            SELECT
            {selectColumns}
            FROM "{tableName}" d
            LEFT JOIN "Users" creator ON creator."Id" = d."CreatedByUserId"{joins}
            WHERE d."IsDeleted" = false
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        // Wrapped in a transaction so a CREATE VIEW failure (a bad join, an invalid column
        // reference) rolls back the preceding DROP too - the old view survives intact
        // instead of being left missing until the next successful publish.
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        // Postgres can't CREATE OR REPLACE a view whose column shape changed (columns
        // added/removed/reordered/retyped) - unlike SQL Server's CREATE OR ALTER VIEW, which
        // handles that transparently. Drop and recreate unconditionally on every publish
        // instead; IF EXISTS covers the first-ever publish, where there's nothing to drop yet.
        await using (var dropCommand = new NpgsqlCommand($"DROP VIEW IF EXISTS \"{viewName}\";", connection, transaction))
            await dropCommand.ExecuteNonQueryAsync(cancellationToken);

        await using (var createCommand = new NpgsqlCommand($"CREATE VIEW \"{viewName}\" AS\n{selectSql};", connection, transaction))
            await createCommand.ExecuteNonQueryAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);

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

    /// <summary>
    /// "lkp_" + field.Code, unless that would overflow Postgres's 63-byte identifier limit -
    /// AddFieldDefinitionCommandValidator now caps new Lookup field Codes short enough to
    /// never hit this path, but a Lookup field created before that cap existed could still
    /// have a Code long enough to overflow once prefixed. Falling back to a deterministic
    /// truncated-code + content-hash suffix here means a view refresh degrades to a slightly
    /// less readable alias for that one legacy field, not a hard crash on every future
    /// publish of the form.
    /// </summary>
    private static string BuildSafeJoinAlias(string fieldCode)
    {
        const string prefix = "lkp_";
        const int maxLength = 63;

        var full = prefix + fieldCode;
        if (full.Length <= maxLength) return full;

        var hash = Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(fieldCode))).ToLowerInvariant()[..8];
        var truncatedCode = fieldCode[..(maxLength - prefix.Length - hash.Length - 1)];
        return $"{prefix}{truncatedCode}_{hash}";
    }

    public async Task<bool> ColumnExistsAsync(string tableName, string columnCode, CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return await ColumnExistsAsync(connection, null, tableName, columnCode, cancellationToken);
    }

    private static async Task<bool> TableExistsAsync(
        NpgsqlConnection connection, NpgsqlTransaction? transaction, string tableName, CancellationToken ct)
    {
        const string sql = """
            SELECT COUNT(1) > 0 FROM INFORMATION_SCHEMA.TABLES
            WHERE TABLE_NAME = @tableName AND TABLE_TYPE = 'BASE TABLE';
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@tableName", tableName);
        return (bool)(await command.ExecuteScalarAsync(ct))!;
    }

    private static async Task<bool> ColumnExistsAsync(
        NpgsqlConnection connection, NpgsqlTransaction? transaction, string tableName, string columnCode, CancellationToken ct)
    {
        const string sql = """
            SELECT COUNT(1) > 0 FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_NAME = @tableName AND COLUMN_NAME = @columnName;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@tableName", tableName);
        command.Parameters.AddWithValue("@columnName", columnCode);
        return (bool)(await command.ExecuteScalarAsync(ct))!;
    }

    private static async Task CreateTableAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, string tableName,
        IReadOnlyCollection<FieldDefinition> fields, CancellationToken ct)
    {
        var columns = new StringBuilder();
        foreach (var field in fields)
        {
            SqlTypeMapper.AssertSafePostgresIdentifier(field.Code);
            var nullability = field.IsRequired ? "NOT NULL" : "NULL";
            columns.Append($",\n    \"{field.Code}\" {SqlTypeMapper.ToPostgresColumnType(field.FieldType)} {nullability}");
        }

        // No DEFAULT on Id, unlike SQL Server's DEFAULT NEWID() - every insert path supplies
        // an explicit client-generated Id already (see DynamicDataRepository.InsertAsync),
        // confirmed via a full audit of every INSERT into a Data_* table before this port, so
        // there's no need for gen_random_uuid() or the extension it would otherwise require.
        // Primary key left unnamed rather than the SQL Server version's explicit
        // "PK_{tableName}" constraint name - Postgres auto-generates one and handles its own
        // length-safe truncation for it, whereas manually building "PK_" + tableName risks
        // exceeding Postgres's 63-byte identifier limit for a tableName already close to it.
        var sql = $"""
            CREATE TABLE "{tableName}" (
                "Id" uuid NOT NULL PRIMARY KEY,
                "FormVersionId" uuid NOT NULL,
                "CreatedAtUtc" timestamptz NOT NULL,
                "CreatedByUserId" uuid NOT NULL,
                "ModifiedAtUtc" timestamptz NULL,
                "ModifiedByUserId" uuid NULL,
                "IsDeleted" boolean NOT NULL DEFAULT false,
                "Extensions" text NULL{columns}
            );
            """;

        await using (var command = new NpgsqlCommand(sql, connection, transaction))
            await command.ExecuteNonQueryAsync(ct);

        // IsDeleted and CreatedAtUtc are filtered/sorted on by every QueryAsync call
        // (DynamicDataRepository) and every Report_* view - not speculative. Index names
        // left unnamed for the same reason the primary key above is: Postgres auto-generates
        // one and truncates it safely, whereas hand-building "IX_" + tableName + "_IsDeleted"
        // risks exceeding the 63-byte identifier limit for a tableName already close to it.
        await using (var isDeletedIndexCommand = new NpgsqlCommand(
            $"CREATE INDEX ON \"{tableName}\" (\"IsDeleted\");", connection, transaction))
            await isDeletedIndexCommand.ExecuteNonQueryAsync(ct);

        await using (var createdAtIndexCommand = new NpgsqlCommand(
            $"CREATE INDEX ON \"{tableName}\" (\"CreatedAtUtc\");", connection, transaction))
            await createdAtIndexCommand.ExecuteNonQueryAsync(ct);
    }

    private static async Task AddColumnAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, string tableName, FieldDefinition field, CancellationToken ct)
    {
        SqlTypeMapper.AssertSafePostgresIdentifier(field.Code);

        // New columns on an already-live table are always nullable, even if the field is
        // marked required going forward - existing rows have no value to backfill, and a
        // NOT NULL ALTER would fail outright. Required-ness is enforced at submission time
        // in the Application layer instead, see SubmitFormDataCommandHandler.
        var sql = $"ALTER TABLE \"{tableName}\" ADD COLUMN \"{field.Code}\" {SqlTypeMapper.ToPostgresColumnType(field.FieldType)} NULL;";

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        await command.ExecuteNonQueryAsync(ct);
    }
}
