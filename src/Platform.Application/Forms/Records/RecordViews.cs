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
    DateTime? EnteredAtUtc = null,
    /// <summary>Repeatable sections: section code -> the record's blocks, in order (2026-11-10).</summary>
    IReadOnlyDictionary<string, IReadOnlyList<RecordBlockDto>>? Sections = null);

/// <summary>One block of a repeatable section. Labels: Lookup/MultiLookup and provider-backed
/// choices resolved to what the person picked from (never a raw GUID or code).</summary>
public record RecordBlockDto(int Index, IReadOnlyDictionary<string, object?> Values, IReadOnlyDictionary<string, string> Labels);

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
        IReadOnlyCollection<FieldDefinition> fields, IReadOnlyList<RecordRow> rows, CancellationToken cancellationToken,
        IFieldOptionsProviders? providers = null)
    {
        if (rows.Count == 0) return Array.Empty<RecordItemDto>();

        var headerFields = fields.Where(f => f.SectionId is null).ToList();
        var labels = await LookupLabelsAsync(db, repository, headerFields, rows, cancellationToken);
        var sections = await SectionBlocksAsync(db, repository, formId, fields, rows, providers, cancellationToken);
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
            entered.TryGetValue(r.Id, out var at) ? at : null,
            sections.TryGetValue(r.Id, out var blocks) ? blocks : null)).ToList();
    }

    private static async Task<Dictionary<Guid, Dictionary<string, string>>> LookupLabelsAsync(
        IApplicationDbContext db, IDynamicDataRepository repository, IReadOnlyCollection<FieldDefinition> fields,
        IReadOnlyList<RecordRow> rows, CancellationToken cancellationToken)
    {
        var result = rows.ToDictionary(r => r.Id, _ => new Dictionary<string, string>());
        var lookups = fields.Where(f => f.FieldType is FieldType.Lookup or FieldType.MultiLookup && f.LookupFormDefinitionId is not null).ToList();
        if (lookups.Count == 0) return result;

        var targetIds = lookups.Select(f => f.LookupFormDefinitionId!.Value).Distinct().ToList();
        var targets = await db.FormDefinitions
            .Include(f => f.Versions).ThenInclude(v => v.Fields)
            .Include(f => f.Versions).ThenInclude(v => v.Sections)
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

    private static async Task<Dictionary<Guid, IReadOnlyDictionary<string, IReadOnlyList<RecordBlockDto>>>> SectionBlocksAsync(
        IApplicationDbContext db, IDynamicDataRepository repository, Guid formId, IReadOnlyCollection<FieldDefinition> fields,
        IReadOnlyList<RecordRow> rows, IFieldOptionsProviders? providers, CancellationToken cancellationToken)
    {
        var result = new Dictionary<Guid, IReadOnlyDictionary<string, IReadOnlyList<RecordBlockDto>>>();
        var sectionIds = fields.Where(f => f.SectionId is not null).Select(f => f.SectionId!.Value).Distinct().ToList();
        if (sectionIds.Count == 0) return result;

        var sections = await db.FormSections.Where(s => sectionIds.Contains(s.Id) && s.ChildTableName != null).ToListAsync(cancellationToken);
        var form = providers is null ? null : await db.FormDefinitions.SingleOrDefaultAsync(f => f.Id == formId, cancellationToken);
        var parentIds = rows.Select(r => r.Id).ToList();
        var perRow = rows.ToDictionary(r => r.Id, _ => new Dictionary<string, IReadOnlyList<RecordBlockDto>>());

        foreach (var section in sections)
        {
            var sectionFields = fields.Where(f => f.SectionId == section.Id).ToList();
            var blocks = await repository.GetBlocksAsync(section.ChildTableName!, sectionFields, parentIds, cancellationToken);
            // Lookup labels for the blocks, through the same reader as the header (one read per target form).
            var blockRows = blocks.Select(b => new RecordRow(b.Id, DateTime.MinValue, Guid.Empty, b.Values)).ToList();
            var lookupLabels = await LookupLabelsAsync(db, repository, sectionFields, blockRows, cancellationToken);
            var multiLabels = await MultiLookupLabelsAsync(db, repository, sectionFields, blocks, cancellationToken);
            foreach (var group in blocks.GroupBy(b => b.ParentId))
            {
                var header = rows.First(r => r.Id == group.Key).Values;
                var list = new List<RecordBlockDto>();
                foreach (var block in group.OrderBy(b => b.BlockIndex))
                {
                    var blockLabels = new Dictionary<string, string>(lookupLabels.GetValueOrDefault(block.Id) ?? new Dictionary<string, string>());
                    foreach (var (code, label) in multiLabels.GetValueOrDefault(block.Id) ?? new Dictionary<string, string>()) blockLabels[code] = label;
                    if (providers is not null && form is not null)
                        await AddProviderLabelsAsync(providers, form, sectionFields, header, block.Values, blockLabels, cancellationToken);
                    list.Add(new RecordBlockDto(block.BlockIndex, block.Values, blockLabels));
                }
                perRow[group.Key][section.Code] = list;
            }
        }
        foreach (var (id, map) in perRow) if (map.Count > 0) result[id] = map;
        return result;
    }

    private static async Task AddProviderLabelsAsync(
        IFieldOptionsProviders providers, FormDefinition form, IEnumerable<FieldDefinition> fields,
        IReadOnlyDictionary<string, object?> header, IReadOnlyDictionary<string, object?> block,
        Dictionary<string, string> labels, CancellationToken ct)
    {
        var merged = new Dictionary<string, object?>(header);
        foreach (var (k, v) in block) merged[k] = v;
        var context = SubmissionPreparer.AsText(merged);
        foreach (var field in fields.Where(f => f.OptionsProvider is not null && f.FieldType is not (FieldType.Lookup or FieldType.MultiLookup)))
        {
            if (providers.Find(field.OptionsProvider) is not { } provider) continue;
            var chosen = block.GetValueOrDefault(field.Code) switch
            {
                string one => new[] { one },
                IEnumerable<string> many => many.ToArray(),
                _ => Array.Empty<string>(),
            };
            if (chosen.Length == 0) continue;
            var options = await provider.GetOptionsAsync(new FieldOptionsContext(form, field, context), ct);
            labels[field.Code] = string.Join(" | ", chosen.Select(c => options.FirstOrDefault(o => o.Value == c) is { } o
                ? (o.LabelEn is null ? o.Label : $"{o.Label}\u001F{o.LabelEn}") : c));
        }
    }

    private static async Task<Dictionary<Guid, Dictionary<string, string>>> MultiLookupLabelsAsync(
        IApplicationDbContext db, IDynamicDataRepository repository, IReadOnlyCollection<FieldDefinition> fields,
        IReadOnlyList<BlockRow> blocks, CancellationToken ct)
    {
        var result = new Dictionary<Guid, Dictionary<string, string>>();
        foreach (var field in fields.Where(f => f.FieldType == FieldType.MultiLookup && f.LookupFormDefinitionId is not null))
        {
            var ids = blocks.SelectMany(b => b.Values.GetValueOrDefault(field.Code) as IEnumerable<Guid> ?? []).Distinct().ToList();
            if (ids.Count == 0) continue;
            var fake = ids.Select(id => new RecordRow(id, DateTime.MinValue, Guid.Empty, new Dictionary<string, object?> { [field.Code] = id })).ToList();
            var single = await LookupLabelsAsync(db, repository, [field], fake, ct);
            foreach (var block in blocks)
            {
                if (block.Values.GetValueOrDefault(field.Code) is not IEnumerable<Guid> picked) continue;
                var names = picked.Select(id => single.GetValueOrDefault(id)?.GetValueOrDefault(field.Code) ?? id.ToString());
                if (!result.TryGetValue(block.Id, out var map)) result[block.Id] = map = new();
                map[field.Code] = string.Join("، ", names);
            }
        }
        return result;
    }

}
