using System.Text.RegularExpressions;
using Platform.Domain.Forms.Enums;

namespace Platform.Infrastructure.Persistence.DynamicSchema;

public static class SqlTypeMapper
{
    private static readonly Regex SafeIdentifier = new("^[A-Za-z][A-Za-z0-9_]{0,127}$", RegexOptions.Compiled);

    /// <summary>
    /// Postgres's own identifier length limit - NAMEDATALEN (64) minus 1 for the null
    /// terminator. Postgres doesn't reject an identifier longer than this - it silently
    /// truncates it, which is a worse failure mode than a loud error here (two long,
    /// similarly-prefixed names - e.g. "Data_"/"Report_" + a form code - could truncate to
    /// the same table/view name and collide).
    /// </summary>
    private const int PostgresMaxIdentifierLength = 63;

    /// <summary>
    /// Attachment fields never reach this - see FieldType and IDynamicSchemaService remarks.
    /// Every other type maps to a fixed, deliberately conservative SQL Server type: nothing
    /// here is derived from user input, so there's no injection surface in the type itself,
    /// only in identifier names (handled by AssertSafeIdentifier).
    /// </summary>
    public static string ToSqlColumnType(FieldType fieldType) => fieldType switch
    {
        FieldType.ShortText => "nvarchar(400)",
        FieldType.LongText => "nvarchar(max)",
        FieldType.Number => "int",
        FieldType.Decimal => "decimal(18,4)",
        FieldType.Boolean => "bit",
        FieldType.DateTime => "datetime2",
        FieldType.Dropdown => "nvarchar(200)",
        FieldType.Lookup => "uniqueidentifier",
        FieldType.Attachment => throw new InvalidOperationException(
            "Attachment fields never get a physical column - resolve via the File Management module."),
        _ => throw new NotSupportedException($"Unmapped FieldType '{fieldType}'.")
    };

    /// <summary>
    /// Postgres equivalent of <see cref="ToSqlColumnType"/> - same deliberately conservative,
    /// fixed-per-FieldType mapping, no user input involved in the type itself.
    /// </summary>
    public static string ToPostgresColumnType(FieldType fieldType) => fieldType switch
    {
        FieldType.ShortText => "varchar(400)",
        FieldType.LongText => "text",
        FieldType.Number => "integer",
        FieldType.Decimal => "numeric(18,4)",
        FieldType.Boolean => "boolean",
        FieldType.DateTime => "timestamptz",
        FieldType.Dropdown => "varchar(200)",
        FieldType.Lookup => "uuid",
        FieldType.Attachment => throw new InvalidOperationException(
            "Attachment fields never get a physical column - resolve via the File Management module."),
        _ => throw new NotSupportedException($"Unmapped FieldType '{fieldType}'.")
    };

    /// <summary>
    /// Hard boundary: table, view, and column names get interpolated directly into DDL
    /// text because SQL doesn't support parameterizing identifiers. Every identifier that
    /// reaches this method should already have passed domain-level normalization
    /// (FormDefinition.Code, FieldDefinition.Code) - this is the last line of defense,
    /// not the first, and it throws rather than "fixing" anything.
    /// </summary>
    public static void AssertSafeIdentifier(string identifier)
    {
        if (!SafeIdentifier.IsMatch(identifier))
            throw new ArgumentException(
                $"'{identifier}' is not a safe SQL identifier - refusing to build DDL with it.", nameof(identifier));
    }

    /// <summary>
    /// Same character-set rule as <see cref="AssertSafeIdentifier"/>, plus Postgres's own
    /// 63-byte identifier limit - deliberately a separate method rather than a parameter on
    /// the existing one, so the SQL Server path's regex/128-char limit above is untouched.
    /// Every character <see cref="SafeIdentifier"/> allows is a single ASCII byte, so
    /// string length and byte length are the same number here. Used throughout
    /// DynamicSchemaService; callers validate the fully prefixed identifier (e.g. "Data_" +
    /// the form code), the same way the SQL Server path always validated post-prefix, not
    /// the raw form/field code alone.
    /// </summary>
    public static void AssertSafePostgresIdentifier(string identifier)
    {
        if (!SafeIdentifier.IsMatch(identifier))
            throw new ArgumentException(
                $"'{identifier}' is not a safe SQL identifier - refusing to build DDL with it.", nameof(identifier));

        if (identifier.Length > PostgresMaxIdentifierLength)
            throw new ArgumentException(
                $"'{identifier}' is {identifier.Length} characters long, which exceeds Postgres's "
                + $"{PostgresMaxIdentifierLength}-byte identifier limit - refusing to build DDL with it rather "
                + "than let Postgres silently truncate it.", nameof(identifier));
    }

    /// <summary>"stock-adjustment" -> "StockAdjustment". Used to derive table/view names from FormDefinition.Code.</summary>
    public static string ToPascalCase(string hyphenatedCode) =>
        string.Concat(hyphenatedCode.Split('-', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => char.ToUpperInvariant(part[0]) + part[1..]));
}
