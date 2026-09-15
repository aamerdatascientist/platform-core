using Platform.Domain.Forms;
using Platform.Domain.Forms.Enums;

namespace Platform.Application.Common.Interfaces;

/// <summary>
/// Turns FormVersion/FieldDefinition metadata into real physical SQL: one table per
/// FormDefinition (see FormDefinition.TableName), created on first publish and evolved on
/// every subsequent publish - and, for an already-published form, at the moment a field is
/// edited. Also owns the human-readable reporting view that Power BI and the (future) AI
/// assistant read from - never the raw table.
///
/// Every method here executes real DDL, and none of it is reversible the way an EF Core
/// migration rollback is. Removal policy, stated explicitly because it changed:
/// **Archive is the default and the recommended path** - deactivate the field so it
/// disappears from the active form and from future submissions, while the column and every
/// historical value stay intact and reportable. <see cref="DropColumnAsync"/> is a rare,
/// heavily-gated escape hatch for cases where the data should never have been collected at
/// all (wrongly-collected personal data, test pollution). It destroys data permanently and
/// is expected to sit behind a type-the-field-name confirmation in any caller that exposes
/// it - see DeleteFieldCommand.
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

    /// <summary>
    /// Adds one column to an already-live table, for a field added to a published form.
    /// Always nullable, for the same reason the publish-time path is: existing rows have no
    /// value to backfill. Idempotent - a column that already exists is left alone.
    /// </summary>
    Task AddColumnForFieldAsync(
        string tableName, FieldDefinition field, CancellationToken cancellationToken = default);

    /// <summary>
    /// ALTER TABLE ... RENAME COLUMN. Preserves every existing value - this is the whole
    /// point of it existing, versus the drop-and-recreate it replaces, which silently threw
    /// away the column's data and reshuffled field order.
    /// </summary>
    Task RenameColumnAsync(
        string tableName, string fromCode, string toCode, CancellationToken cancellationToken = default);

    /// <summary>
    /// Dry run for a type change: returns the Ids of rows whose current value would NOT
    /// survive conversion to <paramref name="newType"/>, capped at <paramref name="maxRows"/>
    /// so a wholly-incompatible column reports a usable sample instead of every row in the
    /// table. Empty means the change is safe. Runs no DDL.
    /// </summary>
    Task<IReadOnlyList<Guid>> FindRowsFailingTypeChangeAsync(
        string tableName, FieldDefinition field, FieldType newType, int maxRows = 10,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// ALTER TABLE ... ALTER COLUMN ... TYPE ... USING. The USING clause is always explicit:
    /// Postgres refuses most cross-type changes without one, and the implicit cases it does
    /// allow aren't the ones this platform needs. Call
    /// <see cref="FindRowsFailingTypeChangeAsync"/> first - this will fail loudly rather than
    /// silently coercing if a value doesn't convert.
    /// </summary>
    Task ChangeColumnTypeAsync(
        string tableName, FieldDefinition field, FieldType newType, CancellationToken cancellationToken = default);

    /// <summary>
    /// Permanently drops a column and everything in it. See the removal policy on this
    /// interface - Archive (deactivating the field) is the default; this is the escape hatch.
    /// </summary>
    Task DropColumnAsync(string tableName, string columnCode, CancellationToken cancellationToken = default);
}
