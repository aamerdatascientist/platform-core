using Microsoft.EntityFrameworkCore;
using Platform.Application.Common.Interfaces;
using Platform.Application.Files.Commands.UploadFile;
using Platform.Domain.Forms;
using Platform.Domain.Forms.Enums;

namespace Platform.Application.Forms.Records;

/// <summary>A submitted record as the redesign's pages show it (My entries 08, Records 20).</summary>
public record RecordItemDto(
    Guid Id,
    Guid FormId,
    DateTime CreatedAtUtc,
    Guid CreatedByUserId,
    string? CreatedByName,
    IReadOnlyDictionary<string, object?> Values,
    /// <summary>Lookup field code -> the chosen row's label (the target form's display field),
    /// so nobody ever sees a raw GUID.</summary>
    IReadOnlyDictionary<string, string> LookupLabels,
    int AttachmentCount,
    /// <summary>When Send was pressed on the phone, for an entry that waited in the offline
    /// queue (owner decision 8: shown instead of the server time); null otherwise.</summary>
    DateTime? EnteredAtUtc = null);

public record RecordsPageDto(IReadOnlyList<RecordItemDto> Items, int TotalCount, int Page, int PageSize);

public record RecordDetailDto(RecordItemDto Record, IReadOnlyList<FileMetadataDto> Attachments);

/// <summary>
/// Turns RecordRows into RecordItemDtos: resolves Lookup values to labels (one read of each
/// target form, capped like GetLookupOptions), counts attachments and names the senders (removed
/// users included, so history keeps its names).
/// </summary>
public static class RecordViews
{
    private const int MaxLookupRows = 2000;

    public static async Task<IReadOnlyList<RecordItemDto>> ToItemsAsync(
        IApplicationDbContext db, IDynamicDataRepository repository, Guid formId,
        IReadOnlyCollection<FieldDefinition> fields, IReadOnlyList<RecordRow> rows, CancellationToken cancellationToken)
    {
        if (rows.Count == 0) return Array.Empty<RecordItemDto>();

        var labels = await LookupLabelsAsync(db, repository, fields, rows, cancellationToken);
        var ids = rows.Select(r => r.Id).ToList();
        var counts = await db.FileMetadataEntries
            .Where(f => ids.Contains(f.RecordId))
            .GroupBy(f => f.RecordId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken);
        var entered = await db.SubmissionReceipts
            .Where(r => r.RecordId != null && ids.Contains(r.RecordId.Value) && r.EnteredAtUtc != null)
            .ToDictionaryAsync(r => r.RecordId!.Value, r => DateTime.SpecifyKind(r.EnteredAtUtc!.Value, DateTimeKind.Utc), cancellationToken);
        var userIds = rows.Select(r => r.CreatedByUserId).Distinct().ToList();
        var names = await db.Users.IgnoreQueryFilters()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName, cancellationToken);

        return rows.Select(r => new RecordItemDto(
            r.Id, formId, r.CreatedAtUtc, r.CreatedByUserId, names.GetValueOrDefault(r.CreatedByUserId), r.Values,
            labels.TryGetValue(r.Id, out var l) ? l : new Dictionary<string, string>(),
            counts.GetValueOrDefault(r.Id),
            entered.TryGetValue(r.Id, out var at) ? at : null)).ToList();
    }

    private static async Task<Dictionary<Guid, Dictionary<string, string>>> LookupLabelsAsync(
        IApplicationDbContext db, IDynamicDataRepository repository, IReadOnlyCollection<FieldDefinition> fields,
        IReadOnlyList<RecordRow> rows, CancellationToken cancellationToken)
    {
        var result = rows.ToDictionary(r => r.Id, _ => new Dictionary<string, string>());
        var lookups = fields.Where(f => f.FieldType == FieldType.Lookup && f.LookupFormDefinitionId is not null).ToList();
        if (lookups.Count == 0) return result;

        var targetIds = lookups.Select(f => f.LookupFormDefinitionId!.Value).Distinct().ToList();
        var targets = await db.FormDefinitions
            .Include(f => f.Versions).ThenInclude(v => v.Fields)
            .Where(f => targetIds.Contains(f.Id))
            .ToListAsync(cancellationToken);

        var labelsByForm = new Dictionary<Guid, Dictionary<Guid, string>>();
        foreach (var target in targets)
        {
            var version = target.GetPublishedVersion();
            if (target.TableName is null || version is null) continue;
            var targetFields = version.Fields.Where(f => f.IsActive).ToList();
            // Same display-field convention as GetLookupOptions: the first active ShortText.
            var display = targetFields.Where(f => f.FieldType == FieldType.ShortText).OrderBy(f => f.DisplayOrder).FirstOrDefault();
            if (display is null) continue;
            var map = new Dictionary<Guid, string>();
            for (var page = 1; map.Count < MaxLookupRows; page++)
            {
                var chunk = await repository.QueryAsync(target.TableName, targetFields, page, 200, null, cancellationToken);
                foreach (var row in chunk.Items)
                    if (row.Values.GetValueOrDefault(display.Code)?.ToString() is { Length: > 0 } text) map[row.Id] = text;
                if (chunk.Items.Count == 0 || page * 200 >= chunk.TotalCount) break;
            }
            labelsByForm[target.Id] = map;
        }

        foreach (var row in rows)
            foreach (var field in lookups)
                if (row.Values.GetValueOrDefault(field.Code) is Guid id
                    && labelsByForm.TryGetValue(field.LookupFormDefinitionId!.Value, out var map)
                    && map.TryGetValue(id, out var label))
                    result[row.Id][field.Code] = label;
        return result;
    }
}
