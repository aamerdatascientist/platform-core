using Microsoft.EntityFrameworkCore;
using Platform.Application.Common.Interfaces;
using Platform.Domain.Forms;

namespace Platform.Application.Analytics;

/// <summary>
/// Small pieces of plumbing shared by the Executive Overview query handlers - resolving a
/// known form by Code, and turning a master-data form's rows into an Id-keyed lookup of
/// display text. Every handler that touches Projects/Materials/Locations as a lookup
/// (rather than as its own primary data) needs both, so it's factored out rather than
/// repeated four times.
/// </summary>
internal static class ExecutiveOverviewSupport
{
    /// <summary>
    /// Null if the form doesn't exist or has never been published - callers treat that as
    /// "no data for this KPI yet", not an error. A dashboard shouldn't 500 just because a
    /// module hasn't been set up.
    /// </summary>
    public static async Task<FormDefinition?> FindPublishedFormAsync(
        IApplicationDbContext db, string code, CancellationToken cancellationToken)
    {
        var form = await db.FormDefinitions
            .Include(f => f.Versions).ThenInclude(v => v.Fields)
            .SingleOrDefaultAsync(f => f.Code == code, cancellationToken);

        return form?.TableName is null ? null : form;
    }

    /// <summary>
    /// Fetches every row of a small master-data form (Projects/Materials/Locations - never
    /// more than a few hundred rows in practice, same single-page assumption FormRenderer's
    /// own Lookup-choice fetch already makes) and maps it to Id -> (Code, Name) for display
    /// resolution. A row whose codeField/nameField value is missing falls back to an empty
    /// string rather than throwing - a dashboard KPI degrading to a blank label beats it
    /// crashing over one malformed row.
    /// </summary>
    public static async Task<Dictionary<Guid, (string Code, string Name)>> ResolveMasterDataAsync(
        IDynamicDataRepository dynamicDataRepository, FormDefinition form,
        string codeField, string nameField, CancellationToken cancellationToken)
    {
        var activeFields = form.GetPublishedVersion()!.Fields.Where(f => f.IsActive).ToList();
        var page = await dynamicDataRepository.QueryAsync(form.TableName!, activeFields, 1, 500, cancellationToken);

        return page.Items.ToDictionary(
            row => row.Id,
            row => (
                Code: row.Values.GetValueOrDefault(codeField)?.ToString() ?? string.Empty,
                Name: row.Values.GetValueOrDefault(nameField)?.ToString() ?? string.Empty));
    }
}
