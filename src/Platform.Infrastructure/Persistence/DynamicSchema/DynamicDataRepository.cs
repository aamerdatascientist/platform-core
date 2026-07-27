using System.Data;
using System.Text.Json;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Platform.Application.Common.Interfaces;
using Platform.Domain.Forms;
using Platform.Domain.Forms.Enums;

namespace Platform.Infrastructure.Persistence.DynamicSchema;

/// <summary>
/// Every column touched here comes from a FieldDefinition.Code the caller already
/// resolved from the database (never from raw user input directly) - see the
/// SubmitFormDataCommandHandler / GetFormSubmissionsQueryHandler callers. AssertSafeIdentifier
/// is still run on each one before it's interpolated, because "trust the caller" is not
/// a security boundary on its own.
/// </summary>
public class DynamicDataRepository : IDynamicDataRepository
{
    private readonly string _connectionString;

    public DynamicDataRepository(IConfiguration configuration) =>
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection is not configured.");

    public async Task<Guid> InsertAsync(
        string tableName,
        IReadOnlyCollection<FieldDefinition> activeFields,
        IReadOnlyDictionary<string, object?> values,
        Guid submittedByUserId,
        CancellationToken cancellationToken = default)
    {
        SqlTypeMapper.AssertSafeIdentifier(tableName);
        var writableFields = activeFields.Where(f => f.FieldType != FieldType.Attachment).ToList();

        var id = Guid.NewGuid();
        var formVersionId = activeFields.First().FormVersionId;

        var parameters = new DynamicParameters();
        parameters.Add("Id", id);
        parameters.Add("FormVersionId", formVersionId);
        parameters.Add("CreatedAtUtc", DateTime.UtcNow);
        parameters.Add("CreatedByUserId", submittedByUserId);

        var columns = new List<string> { "[Id]", "[FormVersionId]", "[CreatedAtUtc]", "[CreatedByUserId]", "[IsDeleted]" };
        var valueTokens = new List<string> { "@Id", "@FormVersionId", "@CreatedAtUtc", "@CreatedByUserId", "0" };

        foreach (var field in writableFields)
        {
            SqlTypeMapper.AssertSafeIdentifier(field.Code);
            var paramName = $"field_{field.Code}";
            columns.Add($"[{field.Code}]");
            valueTokens.Add($"@{paramName}");
            var rawValue = values.TryGetValue(field.Code, out var v) ? v : null;
            // dbType is mandatory here, not just a hint: without it, Dapper infers the SQL
            // type by reflecting on the value's own runtime type, and there's no mapping
            // for a boxed DBNull.Value - it throws NotSupportedException on every null field.
            parameters.Add(paramName, ConvertFieldValue(field.FieldType, rawValue), MapToDbType(field.FieldType));
        }

        var sql = $"""
            INSERT INTO [{tableName}] ({string.Join(", ", columns)})
            OUTPUT INSERTED.[Id]
            VALUES ({string.Join(", ", valueTokens)});
            """;

        await using var connection = new SqlConnection(_connectionString);
        return await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));
    }

    public async Task<DynamicRow?> GetByIdAsync(
        string tableName, IReadOnlyCollection<FieldDefinition> activeFields, Guid id,
        CancellationToken cancellationToken = default)
    {
        SqlTypeMapper.AssertSafeIdentifier(tableName);
        var readableFields = activeFields.Where(f => f.FieldType != FieldType.Attachment).ToList();
        var selectColumns = BuildSelectColumnList(readableFields);

        var sql = $"SELECT [Id]{selectColumns} FROM [{tableName}] WHERE [Id] = @Id AND [IsDeleted] = 0;";

        await using var connection = new SqlConnection(_connectionString);
        var row = await connection.QuerySingleOrDefaultAsync(
            new CommandDefinition(sql, new { Id = id }, cancellationToken: cancellationToken));

        return row is null ? null : ToDynamicRow(row, readableFields);
    }

    public async Task<PagedResult<DynamicRow>> QueryAsync(
        string tableName, IReadOnlyCollection<FieldDefinition> activeFields, int page, int pageSize,
        CancellationToken cancellationToken = default)
    {
        SqlTypeMapper.AssertSafeIdentifier(tableName);
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var readableFields = activeFields.Where(f => f.FieldType != FieldType.Attachment).ToList();
        var selectColumns = BuildSelectColumnList(readableFields);

        var countSql = $"SELECT COUNT(1) FROM [{tableName}] WHERE [IsDeleted] = 0;";
        var pageSql = $"""
            SELECT [Id]{selectColumns} FROM [{tableName}]
            WHERE [IsDeleted] = 0
            ORDER BY [CreatedAtUtc] DESC
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
            """;

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var totalCount = await connection.ExecuteScalarAsync<int>(
            new CommandDefinition(countSql, cancellationToken: cancellationToken));

        IEnumerable<object> rows = await connection.QueryAsync(new CommandDefinition(
            pageSql, new { Offset = (page - 1) * pageSize, PageSize = pageSize }, cancellationToken: cancellationToken));

        var items = rows.Select(row => ToDynamicRow(row, readableFields)).ToList();

        return new PagedResult<DynamicRow>(items, totalCount, page, pageSize);
    }

    private static string BuildSelectColumnList(IEnumerable<FieldDefinition> fields)
    {
        var codes = fields.Select(f =>
        {
            SqlTypeMapper.AssertSafeIdentifier(f.Code);
            return $", [{f.Code}]";
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
            return rawValue;

        if (element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return DBNull.Value;

        return fieldType switch
        {
            FieldType.ShortText or FieldType.LongText or FieldType.Dropdown =>
                (object?)element.GetString() ?? DBNull.Value,
            FieldType.Number => element.GetInt32(),
            FieldType.Decimal => element.GetDecimal(),
            FieldType.Boolean => element.GetBoolean(),
            FieldType.DateTime => element.GetDateTime(),
            FieldType.Lookup => Guid.Parse(element.GetString()!),
            _ => throw new NotSupportedException($"Unsupported field type '{fieldType}' for dynamic data.")
        };
    }

    /// <summary>Mirrors SqlTypeMapper.ToSqlColumnType - keep the two in sync.</summary>
    private static DbType MapToDbType(FieldType fieldType) => fieldType switch
    {
        FieldType.ShortText or FieldType.LongText or FieldType.Dropdown => DbType.String,
        FieldType.Number => DbType.Int32,
        FieldType.Decimal => DbType.Decimal,
        FieldType.Boolean => DbType.Boolean,
        FieldType.DateTime => DbType.DateTime2,
        FieldType.Lookup => DbType.Guid,
        _ => throw new NotSupportedException($"Unsupported field type '{fieldType}' for dynamic data.")
    };
}
