using Microsoft.EntityFrameworkCore;
using Platform.Application.Common.Interfaces;
using Platform.Domain.Forms;
using Platform.Domain.Forms.Enums;

namespace Platform.Application.Forms;

/// <summary>
/// Which FormVersion a field edit applies to, and whether it has to carry real DDL with it.
///
/// A form that has never been published is still being assembled: edits land on its draft,
/// nothing physical exists yet, and all the schema work happens at its first publish -
/// unchanged from how this has always worked. Once a form IS published, there's live data
/// behind it and no useful notion of "staging" a column rename, so edits apply directly to
/// the published version and run their DDL immediately.
///
/// IsLive is what every field-editing command branches on, so the two paths can never drift
/// apart on the question of which version they were supposed to be touching.
/// </summary>
public record FormEditTarget(FormVersion Version, bool IsLive, string? TableName)
{
    /// <summary>
    /// A published form always has a TableName (assigned at first publish and never changed),
    /// so this is safe wherever IsLive is true. Present as a non-null accessor rather than
    /// making every DDL-running command repeat the same null check on something that can't
    /// be null on this path.
    /// </summary>
    public string LiveTableName => TableName
        ?? throw new InvalidOperationException(
            "A published form must have a TableName - this form is published but has none, " +
            "which means its first publish did not complete.");
}

public static class FormEditTargetResolver
{
    /// <summary>
    /// Resolves which version a field edit should modify. Published forms edit live; an
    /// unpublished form edits its draft (and gets the same clean 400 as before if it somehow
    /// has no draft at all).
    /// </summary>
    public static FormEditTarget ResolveFieldEditTarget(this FormDefinition formDefinition)
    {
        if (formDefinition.Status != FormStatus.Published)
            return new FormEditTarget(formDefinition.GetDraftVersionOrThrow(), IsLive: false, formDefinition.TableName);

        var published = formDefinition.GetPublishedVersion()
            ?? throw new Common.Exceptions.ValidationException(new[]
            {
                new FluentValidation.Results.ValidationFailure(
                    nameof(FormDefinition.Id),
                    "This form is marked published but has no published version to edit.")
            });

        return new FormEditTarget(published, IsLive: true, formDefinition.TableName);
    }

    /// <summary>
    /// Finds a field on the version being edited, turning "not on this form" into a clean 404
    /// rather than the domain's InvalidOperationException reaching the generic 500 handler.
    /// Includes archived fields - un-archiving one has to be able to find it.
    /// </summary>
    public static FieldDefinition FindFieldOrThrow(this FormVersion version, Guid fieldDefinitionId) =>
        version.Fields.SingleOrDefault(f => f.Id == fieldDefinitionId)
        ?? throw new Common.Exceptions.NotFoundException(nameof(FieldDefinition), fieldDefinitionId);

    /// <summary>
    /// Loads the FormDefinitions referenced by this version's Lookup fields, with Versions and
    /// Fields included, so RefreshReportingViewAsync can resolve each Lookup to a readable
    /// display value instead of a raw GUID. Every command that refreshes the view goes through
    /// here - the view is rebuilt from scratch on each refresh, so a caller that skipped this
    /// would silently downgrade every Lookup column in the view to raw Ids.
    /// </summary>
    public static async Task<Dictionary<Guid, FormDefinition>> LoadLookupTargetsAsync(
        this IApplicationDbContext db, FormVersion version, CancellationToken cancellationToken)
    {
        var targetIds = version.Fields
            .Where(f => f.IsActive && f.FieldType == FieldType.Lookup)
            .Select(f => f.LookupFormDefinitionId!.Value)
            .Distinct()
            .ToList();

        if (targetIds.Count == 0) return new Dictionary<Guid, FormDefinition>();

        var targets = await db.FormDefinitions
            .Include(f => f.Versions).ThenInclude(v => v.Fields)
            .Where(f => targetIds.Contains(f.Id))
            .ToListAsync(cancellationToken);

        return targets.ToDictionary(f => f.Id);
    }
}
