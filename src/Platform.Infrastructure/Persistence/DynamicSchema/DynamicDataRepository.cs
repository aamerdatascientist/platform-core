using System.Data;
using System.Text.Json;
using Dapper;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Platform.Application.Common.Interfaces;
using Platform.Domain.Forms;
using Platform.Domain.Forms.Enums;

namespace Platform.Infrastructure.Persistence.DynamicSchema;

/// <summary>
/// Every column touched here comes from a FieldDefinition.Code the caller already
/// resolved from the database (never from raw user input directly) - see the
/// SubmitFormDataCommandHandler / GetFormSubmissionsQueryHandler callers.
/// AssertSafePostgresIdentifier is still run on each one before it's interpolated,
/// because "trust the caller" is not a security boundary on its own.
/// </summary>
public class DynamicDataRepository : IDynamicDataRepository
{
    private readonly string _connectionString;

    public DynamicDataRepository(IConfiguration configuration) =>
        _connectionString = configuration.GetConnectionString("PostgresConnection")
            ?? throw new InvalidOperationException("ConnectionStrings:PostgresConnection is not configured.");

    public async Task<Guid> InsertAsync(
        string tableName,
        IReadOnlyCollection<FieldDefinition> activeFields,
        IReadOnlyDictionary<string, object?> values,
        Guid submittedByUserId,
        CancellationToken cancellationToken = default)
    =>
        await InsertWithSectionsAsync(tableName, activeFields, values, Array.Empty<SectionRows>(), submittedByUserId, null, cancellationToken);

    public async Task<bool> UpdateAsync(
        string tableName,
        IReadOnlyCollection<FieldDefinition> activeFields,
        Guid id,
        IReadOnlyDictionary<string, object?> values,
        Guid modifiedByUserId,
        CancellationToken cancellationToken = default)
    =>
        await UpdateWithSectionsAsync(tableName, activeFields, id, values, Array.Empty<SectionRows>(), modifiedByUserId, null, cancellationToken);

    public async Task<bool> SoftDeleteAsync(
        string tableName, Guid id, Guid deletedByUserId, CancellationToken cancellationToken = default)
    {
        SqlTypeMapper.AssertSafePostgresIdentifier(tableName);
        var sql = $"""
            UPDATE "{tableName}"
            SET "IsDeleted" = true, "ModifiedAtUtc" = @ModifiedAtUtc, "ModifiedByUserId" = @ModifiedByUserId
            WHERE "Id" = @Id AND "IsDeleted" = false;
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        var affected = await connection.ExecuteAsync(new CommandDefinition(
            sql, new { Id = id, ModifiedAtUtc = DateTime.UtcNow, ModifiedByUserId = deletedByUserId },
            cancellationToken: cancellationToken));
        return affected > 0;
    }

    public async Task<DynamicRow?> GetByIdAsync(
        string tableName, IReadOnlyCollection<FieldDefinition> activeFields, Guid id,
        CancellationToken cancellationToken = default)
    {
        SqlTypeMapper.AssertSafePostgresIdentifier(tableName);
        var readableFields = ParentColumns(activeFields);
        var selectColumns = BuildSelectColumnList(readableFields);

        var sql = $"SELECT \"Id\"{selectColumns} FROM \"{tableName}\" WHERE \"Id\" = @Id AND \"IsDeleted\" = false;";

        await using var connection = new NpgsqlConnection(_connectionString);
        var row = await connection.QuerySingleOrDefaultAsync(
            new CommandDefinition(sql, new { Id = id }, cancellationToken: cancellationToken));

        return row is null ? null : ToDynamicRow(row, readableFields);
    }

    public async Task<PagedResult<DynamicRow>> QueryAsync(
        string tableName, IReadOnlyCollection<FieldDefinition> activeFields, int page, int pageSize,
        DynamicRowFilter? filter = null, CancellationToken cancellationToken = default)
    {
        SqlTypeMapper.AssertSafePostgresIdentifier(tableName);
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var readableFields = ParentColumns(activeFields);
        var selectColumns = BuildSelectColumnList(readableFields);

        var parameters = new DynamicParameters();
        parameters.Add("Offset", (page - 1) * pageSize);
        parameters.Add("PageSize", pageSize);

        var whereClause = "\"IsDeleted\" = false";
        if (filter is not null)
        {
            // Same identifier boundary as every other column reference here - FieldCode is
            // caller-resolved from a FieldDefinition, but that's not a security boundary on
            // its own (see the class doc comment).
            SqlTypeMapper.AssertSafePostgresIdentifier(filter.FieldCode);
            whereClause += $" AND \"{filter.FieldCode}\" = @FilterValue";
            parameters.Add("FilterValue", filter.Value);
        }

        var countSql = $"SELECT COUNT(1) FROM \"{tableName}\" WHERE {whereClause};";
        var pageSql = $"""
            SELECT "Id"{selectColumns} FROM "{tableName}"
            WHERE {whereClause}
            ORDER BY "CreatedAtUtc" DESC
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var totalCount = await connection.ExecuteScalarAsync<int>(
            new CommandDefinition(countSql, parameters, cancellationToken: cancellationToken));

        IEnumerable<object> rows = await connection.QueryAsync(new CommandDefinition(
            pageSql, parameters, cancellationToken: cancellationToken));

        var items = rows.Select(row => ToDynamicRow(row, readableFields)).ToList();

        return new PagedResult<DynamicRow>(items, totalCount, page, pageSize);
    }

    public async Task<PagedResult<RecordRow>> SearchAsync(
        string tableName, IReadOnlyCollection<FieldDefinition> activeFields, RecordSearch search,
        CancellationToken cancellationToken = default)
    {
        SqlTypeMapper.AssertSafePostgresIdentifier(tableName);
        var page = Math.Max(search.Page, 1);
        var pageSize = Math.Clamp(search.PageSize, 1, 200);
        var readableFields = ParentColumns(activeFields);
        var selectColumns = BuildSelectColumnList(readableFields);

        var parameters = new DynamicParameters();
        parameters.Add("Offset", (page - 1) * pageSize);
        parameters.Add("PageSize", pageSize);
        var where = new List<string> { "\"IsDeleted\" = false" };

        var i = 0;
        foreach (var filter in search.Matches ?? Array.Empty<DynamicRowFilter>())
        {
            SqlTypeMapper.AssertSafePostgresIdentifier(filter.FieldCode);
            where.Add($"\"{filter.FieldCode}\" = @Eq{i}");
            parameters.Add($"Eq{i}", filter.Value);
            i++;
        }

        if (!string.IsNullOrWhiteSpace(search.Text))
        {
            var textColumns = readableFields
                .Where(f => f.FieldType is FieldType.ShortText or FieldType.LongText or FieldType.Dropdown)
                .Select(f =>
                {
                    SqlTypeMapper.AssertSafePostgresIdentifier(f.Code);
                    return $"\"{f.Code}\" ILIKE @Text ESCAPE '\\'";
                })
                .ToList();
            // No text column means nothing can match - not "ignore the search".
            where.Add(textColumns.Count == 0 ? "false" : $"({string.Join(" OR ", textColumns)})");
            var escaped = search.Text.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
            parameters.Add("Text", $"%{escaped}%");
        }

        var dateColumn = "\"CreatedAtUtc\"";
        if (search.DateFieldCode is { } dateCode)
        {
            SqlTypeMapper.AssertSafePostgresIdentifier(dateCode);
            dateColumn = $"\"{dateCode}\"";
        }
        if (search.From is { } from)
        {
            where.Add($"{dateColumn} >= @From");
            parameters.Add("From", search.DateFieldCode is null ? from.UtcDateTime : from.ToUniversalTime());
        }
        if (search.To is { } to)
        {
            where.Add($"{dateColumn} < @To");
            parameters.Add("To", search.DateFieldCode is null ? to.UtcDateTime : to.ToUniversalTime());
        }

        if (search.CreatedByUserId is { } userId)
        {
            where.Add("\"CreatedByUserId\" = @CreatedBy");
            parameters.Add("CreatedBy", userId);
        }

        if (search.Before is { } before)
        {
            where.Add("(\"CreatedAtUtc\", \"Id\") < (@BeforeAt, @BeforeId)");
            parameters.Add("BeforeAt", DateTime.SpecifyKind(before.CreatedAtUtc, DateTimeKind.Utc));
            parameters.Add("BeforeId", before.Id);
        }

        var whereClause = string.Join(" AND ", where);
        var pageSql = $"""
            SELECT "Id", "CreatedAtUtc", "CreatedByUserId"{selectColumns} FROM "{tableName}"
            WHERE {whereClause}
            ORDER BY "CreatedAtUtc" DESC, "Id" DESC
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        var total = search.CountTotal
            ? await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                $"SELECT COUNT(1) FROM \"{tableName}\" WHERE {whereClause};", parameters, cancellationToken: cancellationToken))
            : -1;
        IEnumerable<object> rows = await connection.QueryAsync(new CommandDefinition(pageSql, parameters, cancellationToken: cancellationToken));
        return new PagedResult<RecordRow>(rows.Select(r => ToRecordRow(r, readableFields)).ToList(), total, page, pageSize);
    }

    public async Task<RecordRow?> GetRecordAsync(
        string tableName, IReadOnlyCollection<FieldDefinition> activeFields, Guid id,
        CancellationToken cancellationToken = default)
    {
        SqlTypeMapper.AssertSafePostgresIdentifier(tableName);
        var readableFields = ParentColumns(activeFields);
        var sql = $"SELECT \"Id\", \"CreatedAtUtc\", \"CreatedByUserId\"{BuildSelectColumnList(readableFields)} FROM \"{tableName}\" WHERE \"Id\" = @Id AND \"IsDeleted\" = false;";
        await using var connection = new NpgsqlConnection(_connectionString);
        var row = await connection.QuerySingleOrDefaultAsync(new CommandDefinition(sql, new { Id = id }, cancellationToken: cancellationToken));
        return row is null ? null : ToRecordRow(row, readableFields);
    }

    private static RecordRow ToRecordRow(dynamic row, IReadOnlyCollection<FieldDefinition> fields)
    {
        var dict = (IDictionary<string, object?>)row;
        var values = fields.ToDictionary(f => f.Code, f => dict.TryGetValue(f.Code, out var v) ? v : null);
        return new RecordRow(
            (Guid)dict["Id"]!,
            DateTime.SpecifyKind((DateTime)dict["CreatedAtUtc"]!, DateTimeKind.Utc),
            (Guid)dict["CreatedByUserId"]!,
            values);
    }

    public async Task<int> CountAsync(string tableName, CancellationToken cancellationToken = default)
    {
        SqlTypeMapper.AssertSafePostgresIdentifier(tableName);
        var countSql = $"SELECT COUNT(1) FROM \"{tableName}\" WHERE \"IsDeleted\" = false;";

        await using var connection = new NpgsqlConnection(_connectionString);
        return await connection.ExecuteScalarAsync<int>(
            new CommandDefinition(countSql, cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<SubmissionStamp>> GetSubmissionStampsAsync(
        string tableName, IReadOnlyCollection<Guid> userIds, DateTime fromUtc, DateTime toUtc,
        CancellationToken cancellationToken = default)
    {
        SqlTypeMapper.AssertSafePostgresIdentifier(tableName);
        if (userIds.Count == 0) return Array.Empty<SubmissionStamp>();

        var sql = $"""
            SELECT "CreatedByUserId" AS "UserId", "CreatedAtUtc", "Id" FROM "{tableName}"
            WHERE "IsDeleted" = false AND "CreatedByUserId" = ANY(@UserIds)
              AND "CreatedAtUtc" >= @FromUtc AND "CreatedAtUtc" < @ToUtc;
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        var rows = await connection.QueryAsync<(Guid UserId, DateTime CreatedAtUtc, Guid Id)>(new CommandDefinition(
            sql,
            new
            {
                UserIds = userIds.ToArray(),
                FromUtc = DateTime.SpecifyKind(fromUtc, DateTimeKind.Utc),
                ToUtc = DateTime.SpecifyKind(toUtc, DateTimeKind.Utc),
            },
            cancellationToken: cancellationToken));
        return rows.Select(r => new SubmissionStamp(r.UserId, DateTime.SpecifyKind(r.CreatedAtUtc, DateTimeKind.Utc), r.Id)).ToList();
    }

    public async Task<IReadOnlyList<string>> GetDistinctColumnValuesAsync(
        string tableName, string columnCode, CancellationToken cancellationToken = default)
    {
        SqlTypeMapper.AssertSafePostgresIdentifier(tableName);
        SqlTypeMapper.AssertSafePostgresIdentifier(columnCode);

        // Text-only by design (see interface doc) - a dynamic options source always points at
        // another Dropdown/ShortText column, never Number/Lookup/etc, so there's no FieldType
        // to branch on here the way ToDynamicRow/ConvertFieldValue do.
        var sql = $"""
            SELECT DISTINCT "{columnCode}" FROM "{tableName}"
            WHERE "IsDeleted" = false AND "{columnCode}" IS NOT NULL AND "{columnCode}" <> ''
            ORDER BY "{columnCode}";
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        var values = await connection.QueryAsync<string>(
            new CommandDefinition(sql, cancellationToken: cancellationToken));
        return values.ToList();
    }

    public async Task<bool> AnyOtherWithTextAsync(
        string tableName, string columnCode, string text,
        string? scopeColumnCode, object? scopeValue, Guid? excludeId,
        CancellationToken cancellationToken = default)
    {
        SqlTypeMapper.AssertSafePostgresIdentifier(tableName);
        SqlTypeMapper.AssertSafePostgresIdentifier(columnCode);

        var parameters = new DynamicParameters();
        parameters.Add("Text", text.Trim());
        parameters.Add("ExcludeId", excludeId ?? Guid.Empty);

        var whereClause = $"\"IsDeleted\" = false AND \"Id\" <> @ExcludeId AND lower(btrim(\"{columnCode}\")) = lower(@Text)";
        if (scopeColumnCode is not null)
        {
            SqlTypeMapper.AssertSafePostgresIdentifier(scopeColumnCode);
            if (scopeValue is null)
            {
                whereClause += $" AND \"{scopeColumnCode}\" IS NULL";
            }
            else
            {
                whereClause += $" AND \"{scopeColumnCode}\" = @ScopeValue";
                parameters.Add("ScopeValue", scopeValue);
            }
        }

        var sql = $"SELECT EXISTS (SELECT 1 FROM \"{tableName}\" WHERE {whereClause});";

        await using var connection = new NpgsqlConnection(_connectionString);
        return await connection.ExecuteScalarAsync<bool>(
            new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));
    }

    /// <summary>The fields that have a column in the parent table: not attachments, not in a section.</summary>
    private static List<FieldDefinition> ParentColumns(IEnumerable<FieldDefinition> fields) =>
        fields.Where(f => f.FieldType != FieldType.Attachment && f.SectionId is null).ToList();

    private static List<FieldDefinition> BlockColumns(IEnumerable<FieldDefinition> fields) =>
        fields.Where(f => f.FieldType != FieldType.Attachment).ToList();

    public async Task<Guid> InsertWithSectionsAsync(
        string tableName, IReadOnlyCollection<FieldDefinition> activeFields, IReadOnlyDictionary<string, object?> values,
        IReadOnlyList<SectionRows> sections, Guid submittedByUserId, WriteGuard? guard = null,
        CancellationToken cancellationToken = default)
    {
        SqlTypeMapper.AssertSafePostgresIdentifier(tableName);
        var fields = ParentColumns(activeFields);
        var id = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var parameters = new DynamicParameters();
        parameters.Add("Id", id);
        parameters.Add("FormVersionId", activeFields.First().FormVersionId);
        parameters.Add("CreatedAtUtc", now);
        parameters.Add("CreatedByUserId", submittedByUserId);
        var columns = new List<string> { "\"Id\"", "\"FormVersionId\"", "\"CreatedAtUtc\"", "\"CreatedByUserId\"", "\"IsDeleted\"" };
        var tokens = new List<string> { "@Id", "@FormVersionId", "@CreatedAtUtc", "@CreatedByUserId", "false" };
        AddValueParameters(fields, values, parameters, columns, tokens);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await TakeGuardAsync(connection, transaction, guard, cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            $"INSERT INTO \"{tableName}\" ({string.Join(", ", columns)}) VALUES ({string.Join(", ", tokens)});",
            parameters, transaction, cancellationToken: cancellationToken));
        foreach (var section in sections)
            await InsertBlocksAsync(connection, transaction, section, id, submittedByUserId, now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return id;
    }

    public async Task<bool> UpdateWithSectionsAsync(
        string tableName, IReadOnlyCollection<FieldDefinition> activeFields, Guid id, IReadOnlyDictionary<string, object?> values,
        IReadOnlyList<SectionRows> sections, Guid modifiedByUserId, WriteGuard? guard = null,
        CancellationToken cancellationToken = default)
    {
        SqlTypeMapper.AssertSafePostgresIdentifier(tableName);
        var fields = ParentColumns(activeFields);
        var now = DateTime.UtcNow;
        var parameters = new DynamicParameters();
        parameters.Add("Id", id);
        parameters.Add("ModifiedAtUtc", now);
        parameters.Add("ModifiedByUserId", modifiedByUserId);
        var assignments = new List<string> { "\"ModifiedAtUtc\" = @ModifiedAtUtc", "\"ModifiedByUserId\" = @ModifiedByUserId" };
        foreach (var field in fields)
        {
            SqlTypeMapper.AssertSafePostgresIdentifier(field.Code);
            var paramName = $"field_{field.Code}";
            assignments.Add($"\"{field.Code}\" = @{paramName}");
            AddParameter(parameters, paramName, field.FieldType, values.TryGetValue(field.Code, out var v) ? v : null);
        }

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await TakeGuardAsync(connection, transaction, guard, cancellationToken);
        var affected = await connection.ExecuteAsync(new CommandDefinition(
            $"UPDATE \"{tableName}\" SET {string.Join(", ", assignments)} WHERE \"Id\" = @Id AND \"IsDeleted\" = false;",
            parameters, transaction, cancellationToken: cancellationToken));
        if (affected == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }
        foreach (var section in sections)
        {
            SqlTypeMapper.AssertSafePostgresIdentifier(section.ChildTableName);
            await connection.ExecuteAsync(new CommandDefinition(
                $"UPDATE \"{section.ChildTableName}\" SET \"IsDeleted\" = true, \"ModifiedAtUtc\" = @Now, \"ModifiedByUserId\" = @By WHERE \"ParentId\" = @Id AND \"IsDeleted\" = false;",
                new { Id = id, Now = now, By = modifiedByUserId }, transaction, cancellationToken: cancellationToken));
            await InsertBlocksAsync(connection, transaction, section, id, modifiedByUserId, now, cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private static async Task InsertBlocksAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, SectionRows section, Guid parentId, Guid userId, DateTime now,
        CancellationToken cancellationToken)
    {
        SqlTypeMapper.AssertSafePostgresIdentifier(section.ChildTableName);
        var fields = BlockColumns(section.Fields);
        for (var index = 0; index < section.Blocks.Count; index++)
        {
            var parameters = new DynamicParameters();
            parameters.Add("Id", Guid.NewGuid());
            parameters.Add("ParentId", parentId);
            parameters.Add("BlockIndex", index);
            parameters.Add("CreatedAtUtc", now);
            parameters.Add("CreatedByUserId", userId);
            var columns = new List<string> { "\"Id\"", "\"ParentId\"", "\"BlockIndex\"", "\"CreatedAtUtc\"", "\"CreatedByUserId\"", "\"IsDeleted\"" };
            var tokens = new List<string> { "@Id", "@ParentId", "@BlockIndex", "@CreatedAtUtc", "@CreatedByUserId", "false" };
            AddValueParameters(fields, section.Blocks[index], parameters, columns, tokens);
            await connection.ExecuteAsync(new CommandDefinition(
                $"INSERT INTO \"{section.ChildTableName}\" ({string.Join(", ", columns)}) VALUES ({string.Join(", ", tokens)});",
                parameters, transaction, cancellationToken: cancellationToken));
        }
    }

    private static void AddValueParameters(
        IEnumerable<FieldDefinition> fields, IReadOnlyDictionary<string, object?> values, DynamicParameters parameters,
        List<string> columns, List<string> tokens)
    {
        foreach (var field in fields)
        {
            SqlTypeMapper.AssertSafePostgresIdentifier(field.Code);
            var paramName = $"field_{field.Code}";
            columns.Add($"\"{field.Code}\"");
            tokens.Add($"@{paramName}");
            AddParameter(parameters, paramName, field.FieldType, values.TryGetValue(field.Code, out var v) ? v : null);
        }
    }

    private static void AddParameter(DynamicParameters parameters, string name, FieldType type, object? raw)
    {
        var value = ConvertFieldValue(type, raw);
        // Arrays: Npgsql infers text[] / uuid[] from the CLR type; a DbType would break that.
        if (type is FieldType.MultiSelect or FieldType.MultiLookup)
        {
            if (value is DBNull) parameters.Add(name, type == FieldType.MultiSelect ? (object)Array.Empty<string>() : Array.Empty<Guid>());
            else parameters.Add(name, value);
        }
        else parameters.Add(name, value, MapToDbType(type));
    }

    private static async Task TakeGuardAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, WriteGuard? guard, CancellationToken ct)
    {
        if (guard is null) return;
        if (!string.IsNullOrWhiteSpace(guard.LockKey))
            await connection.ExecuteAsync(new CommandDefinition(
                "SELECT pg_advisory_xact_lock(hashtext(@Key));", new { Key = guard.LockKey }, transaction, cancellationToken: ct));
        if (guard.WhileLocked is not null) await guard.WhileLocked(ct);
    }

    public async Task<IReadOnlyList<BlockRow>> GetBlocksAsync(
        string childTableName, IReadOnlyCollection<FieldDefinition> sectionFields, IReadOnlyCollection<Guid> parentIds,
        CancellationToken cancellationToken = default)
    {
        SqlTypeMapper.AssertSafePostgresIdentifier(childTableName);
        if (parentIds.Count == 0) return Array.Empty<BlockRow>();
        var fields = BlockColumns(sectionFields);
        var sql = $"""
            SELECT "Id", "ParentId", "BlockIndex"{BuildSelectColumnList(fields)} FROM "{childTableName}"
            WHERE "IsDeleted" = false AND "ParentId" = ANY(@Ids)
            ORDER BY "ParentId", "BlockIndex";
            """;
        await using var connection = new NpgsqlConnection(_connectionString);
        var rows = await connection.QueryAsync(new CommandDefinition(sql, new { Ids = parentIds.ToArray() }, cancellationToken: cancellationToken));
        return rows.Select(r => (BlockRow)ToBlockRow(r, fields)).ToList();
    }

    public async Task<IReadOnlyList<BlockRow>> GetAllBlocksAsync(
        string childTableName, string parentTableName, IReadOnlyCollection<FieldDefinition> sectionFields,
        CancellationToken cancellationToken = default)
    {
        SqlTypeMapper.AssertSafePostgresIdentifier(childTableName);
        SqlTypeMapper.AssertSafePostgresIdentifier(parentTableName);
        var fields = BlockColumns(sectionFields);
        var columns = string.Concat(fields.Select(f => $", c.\"{f.Code}\""));
        var sql = $"""
            SELECT c."Id", c."ParentId", c."BlockIndex"{columns} FROM "{childTableName}" c
            JOIN "{parentTableName}" p ON p."Id" = c."ParentId" AND p."IsDeleted" = false
            WHERE c."IsDeleted" = false
            ORDER BY c."ParentId", c."BlockIndex";
            """;
        await using var connection = new NpgsqlConnection(_connectionString);
        var rows = await connection.QueryAsync(new CommandDefinition(sql, cancellationToken: cancellationToken));
        return rows.Select(r => (BlockRow)ToBlockRow(r, fields)).ToList();
    }

    private static BlockRow ToBlockRow(dynamic row, IReadOnlyCollection<FieldDefinition> fields)
    {
        var dict = (IDictionary<string, object?>)row;
        var values = fields.ToDictionary(f => f.Code, f => dict.TryGetValue(f.Code, out var v) ? v : null);
        return new BlockRow((Guid)dict["Id"]!, (Guid)dict["ParentId"]!, (int)dict["BlockIndex"]!, values);
    }

    private static string BuildSelectColumnList(IEnumerable<FieldDefinition> fields)
    {
        var codes = fields.Select(f =>
        {
            SqlTypeMapper.AssertSafePostgresIdentifier(f.Code);
            return $", \"{f.Code}\"";
        });
        return string.Concat(codes);
    }

    private static DynamicRow ToDynamicRow(dynamic row, IReadOnlyCollection<FieldDefinition> fields)
    {
        var dict = (IDictionary<string, object?>)row;
        var id = (Guid)dict["Id"]!;
        var values = fields.ToDictionary(f => f.Code, f => dict.TryGetValue(f.Code, out var v) ? v : null);
        return new DynamicRow(id, values);
    }

    /// <summary>
    /// Values arriving from the API controller's JSON-bound dictionary are boxed
    /// JsonElement, not plain CLR types - Dapper can't map JsonElement to a DbType, so it
    /// needs unwrapping into the CLR type the field's physical column actually expects.
    /// </summary>
    private static object ConvertFieldValue(FieldType fieldType, object? rawValue)
    {
        if (rawValue is null) return DBNull.Value;

        if (rawValue is not JsonElement element)
            return rawValue switch
            {
                IEnumerable<string> texts when fieldType == FieldType.MultiSelect => texts.ToArray(),
                IEnumerable<Guid> ids when fieldType == FieldType.MultiLookup => ids.ToArray(),
                // Npgsql writes only UTC offsets to timestamptz (CLAUDE.md); server-side callers
                // (module hooks) may hand over a Saudi-time value.
                DateTimeOffset dto => dto.ToUniversalTime(),
                DateTime { Kind: DateTimeKind.Unspecified } dt => new DateTimeOffset(dt, SaudiArabiaOffset).ToUniversalTime(),
                DateTime dt => new DateTimeOffset(dt.ToUniversalTime()),
                _ => rawValue,
            };

        if (element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return DBNull.Value;

        return fieldType switch
        {
            FieldType.ShortText or FieldType.LongText or FieldType.Dropdown =>
                (object?)element.GetString() ?? DBNull.Value,
            FieldType.Number => element.GetInt32(),
            FieldType.Decimal => element.GetDecimal(),
            FieldType.Boolean => element.GetBoolean(),
            FieldType.DateTime => ToDateTimeOffset(element),
            FieldType.Lookup => Guid.Parse(element.GetString()!),
            FieldType.MultiSelect => element.ValueKind == JsonValueKind.Array
                ? element.EnumerateArray().Select(e => e.GetString() ?? string.Empty).Where(t => t.Length > 0).Distinct().ToArray()
                : new[] { element.GetString() ?? string.Empty },
            FieldType.MultiLookup => element.ValueKind == JsonValueKind.Array
                ? element.EnumerateArray().Select(e => Guid.Parse(e.GetString()!)).Distinct().ToArray()
                : new[] { Guid.Parse(element.GetString()!) },
            _ => throw new NotSupportedException($"Unsupported field type '{fieldType}' for dynamic data.")
        };
    }

    /// <summary>
    /// Dynamic form DateTime fields assume Saudi local time (Arabia Standard Time, UTC+03:00,
    /// no DST - stable year-round, safe to hardcode rather than resolve via TimeZoneInfo) when
    /// the incoming value carries no explicit offset. This is a deliberate, form-data-specific
    /// choice, not an oversight or an inconsistency with the static schema's audit columns
    /// (CreatedAtUtc/ModifiedAtUtc), which are and remain UTC.
    /// </summary>
    private static readonly TimeSpan SaudiArabiaOffset = TimeSpan.FromHours(3);

    private static DateTimeOffset ToDateTimeOffset(JsonElement element)
    {
        var dateTime = element.GetDateTime();
        var offset = dateTime.Kind == DateTimeKind.Unspecified
            ? new DateTimeOffset(dateTime, SaudiArabiaOffset)
            : element.GetDateTimeOffset();

        // Npgsql only accepts Offset=0 for timestamptz - ToUniversalTime() re-expresses the
        // same point in time with Offset=Zero rather than changing what instant it represents,
        // so the Saudi-local-time interpretation above still happens first, this just
        // normalizes the result for the wire.
        return offset.ToUniversalTime();
    }

    /// <summary>Mirrors SqlTypeMapper.ToPostgresColumnType - keep the two in sync.</summary>
    private static DbType MapToDbType(FieldType fieldType) => fieldType switch
    {
        FieldType.ShortText or FieldType.LongText or FieldType.Dropdown => DbType.String,
        FieldType.Number => DbType.Int32,
        FieldType.Decimal => DbType.Decimal,
        FieldType.Boolean => DbType.Boolean,
        FieldType.DateTime => DbType.DateTimeOffset,
        FieldType.Lookup => DbType.Guid,
        _ => throw new NotSupportedException($"Unsupported field type '{fieldType}' for dynamic data.")
    };
}
