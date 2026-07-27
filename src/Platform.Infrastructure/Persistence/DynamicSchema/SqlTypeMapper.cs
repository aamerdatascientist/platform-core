using System.Text.RegularExpressions;
using Platform.Domain.Forms.Enums;

namespace Platform.Infrastructure.Persistence.DynamicSchema;

public static class SqlTypeMapper
{
    private static readonly Regex SafeIdentifier = new("^[A-Za-z][A-Za-z0-9_]{0,127}$", RegexOptions.Compiled);

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

    /// <summary>"stock-adjustment" -> "StockAdjustment". Used to derive table/view names from FormDefinition.Code.</summary>
    public static string ToPascalCase(string hyphenatedCode) =>
        string.Concat(hyphenatedCode.Split('-', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => char.ToUpperInvariant(part[0]) + part[1..]));
}
