using Platform.Domain.Forms;

namespace Platform.Application.Common.Interfaces;

/// <summary>
/// Turns FormVersion/FieldDefinition metadata into real physical SQL: one table per
/// FormDefinition (see FormDefinition.TableName), created on first publish and evolved
/// additively on every subsequent publish. Also owns the human-readable reporting view
/// that Power BI and the (future) AI assistant read from - never the raw table.
///
/// Every method here executes real DDL. Nothing about this is reversible in the way an
/// EF Core migration rollback is, which is why field removal is modeled as
/// "deactivate + hide from the view", never a physical DROP COLUMN.
/// </summary>
public interface IDynamicSchemaService
{
    /// <summary>
    /// Idempotent: if the FormDefinition's table already exists, this only adds columns
    /// for fields that don't yet exist on it (keyed by FieldDefinition.Code). Returns the
    /// physical table name that was created or confirmed.
    /// </summary>
    Task<string> EnsureTableForPublishedVersionAsync(
        FormDefinition formDefinition, FormVersion version, CancellationToken cancellationToken = default);

    /// <summary>
    /// Regenerates the reporting view (e.g. "Report_StockAdjustment") from the current active
    /// fields. Lookup fields are shown as the raw referenced record's Id unless the caller
    /// supplies <paramref name="lookupTargets"/> (keyed by FormDefinition.Id, with Versions and
    /// Fields loaded) - when a Lookup field's target is present there, the view instead joins
    /// to the target form's table and shows its first active ShortText field.
    /// </summary>
    Task RefreshReportingViewAsync(
        FormDefinition formDefinition, FormVersion version,
        IReadOnlyDictionary<Guid, FormDefinition>? lookupTargets = null,
        CancellationToken cancellationToken = default);

    /// <summary>True if the physical table for this form already has a column for the given field code.</summary>
    Task<bool> ColumnExistsAsync(string tableName, string columnCode, CancellationToken cancellationToken = default);
}
