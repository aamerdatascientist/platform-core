using System.Globalization;
using System.Text.Json;
using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;
using Platform.Application.Common.Interfaces;
using Platform.Domain.Forms;
using Platform.Domain.Forms.Enums;

namespace Platform.Application.Forms;

/// <summary>The blocks of one repeatable section, normalized.</summary>
public sealed record PreparedSection(FormSection Section, IReadOnlyList<FieldDefinition> Fields, IReadOnlyList<IReadOnlyDictionary<string, object?>> Blocks);

/// <summary>A submission after validation and normalization: what gets written, and what
/// form hooks (IFormSubmissionHook) check.</summary>
public sealed record PreparedSubmission(
    FormDefinition Form,
    FormVersion Version,
    IReadOnlyList<FieldDefinition> HeaderFields,
    IReadOnlyDictionary<string, object?> Header,
    IReadOnlyList<PreparedSection> Sections)
{
    public IReadOnlyList<IReadOnlyDictionary<string, object?>> BlocksOf(string sectionCode) =>
        Sections.FirstOrDefault(s => s.Section.Code == sectionCode)?.Blocks ?? Array.Empty<IReadOnlyDictionary<string, object?>>();

    public IReadOnlyList<SectionRows> ToSectionRows() =>
        Sections.Select(s => new SectionRows(s.Section.ChildTableName!, s.Fields, s.Blocks)).ToList();
}

/// <summary>
/// Turns a raw submission into a <see cref="PreparedSubmission"/> (added 2026-11-10 with
/// repeatable sections). A section's blocks travel under the section's code as a JSON array of
/// objects keyed by field code. Every block is validated on its own, with the header's values
/// visible to it (a block field can be shown by, or filtered by, a header field); failures are
/// keyed <c>{section}[{index}].{field}</c> so the form can point at the right block.
/// </summary>
public sealed class SubmissionPreparer(IApplicationDbContext db, IDynamicDataRepository repository, IFieldOptionsProviders providers)
{
    public async Task<PreparedSubmission> PrepareAsync(
        FormDefinition form, FormVersion version, IReadOnlyDictionary<string, object?> values, CancellationToken cancellationToken)
    {
        var active = version.Fields.Where(f => f.IsActive).ToList();
        var headerFields = active.Where(f => f.SectionId is null).ToList();
        var sections = version.Sections.Where(s => active.Any(f => f.SectionId == s.Id)).ToList();

        var header = values.Where(kv => sections.All(s => s.Code != kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
        var errors = new List<ValidationFailure>();

        var headerAttributes = await ResolveAttributesAsync(form, active, header, cancellationToken);
        Collect(errors, "", SubmissionValueValidator.Validate(headerFields, header, headerAttributes));

        var rawBlocks = new Dictionary<FormSection, List<Dictionary<string, object?>>>();
        foreach (var section in sections)
        {
            var blocks = ReadBlocks(values.GetValueOrDefault(section.Code), out var malformed);
            if (malformed)
            {
                errors.Add(new ValidationFailure(section.Code, $"'{section.TitleAr}' must be a list of entries."));
                continue;
            }
            if (blocks.Count < section.MinBlocks)
                errors.Add(new ValidationFailure(section.Code, $"Add at least {section.MinBlocks} to '{section.TitleAr}'."));
            if (blocks.Count > section.MaxBlocks)
                errors.Add(new ValidationFailure(section.Code, $"'{section.TitleAr}' can have at most {section.MaxBlocks}."));
            rawBlocks[section] = blocks;

            var sectionFields = active.Where(f => f.SectionId == section.Id).ToList();
            for (var i = 0; i < blocks.Count; i++)
            {
                var merged = Merge(header, blocks[i]);
                var attributes = await ResolveAttributesAsync(form, active, merged, cancellationToken);
                Collect(errors, $"{section.Code}[{i}].", SubmissionValueValidator.Validate(sectionFields, merged, attributes));
            }
        }
        if (errors.Count != 0) throw new Common.Exceptions.ValidationException(errors);

        await CheckBlockedChoicesAsync(form, active, headerFields, header, "", cancellationToken);
        foreach (var (section, blocks) in rawBlocks)
            for (var i = 0; i < blocks.Count; i++)
                await CheckBlockedChoicesAsync(form, active, active.Where(f => f.SectionId == section.Id).ToList(), Merge(header, blocks[i]), $"{section.Code}[{i}].", cancellationToken);

        var normalizedHeader = SubmissionValueValidator.Normalize(headerFields, header);
        await SubmissionDataChecksForHeaderAsync(form, headerFields, normalizedHeader, cancellationToken);
        await CheckProviderChoicesAsync(form, headerFields, header, "", cancellationToken);

        var prepared = new List<PreparedSection>();
        foreach (var (section, blocks) in rawBlocks)
        {
            var sectionFields = active.Where(f => f.SectionId == section.Id).ToList();
            var normalizedBlocks = new List<IReadOnlyDictionary<string, object?>>();
            for (var i = 0; i < blocks.Count; i++)
            {
                var merged = Merge(header, blocks[i]);
                var normalizedMerged = SubmissionValueValidator.Normalize(active.Where(f => f.SectionId is null || f.SectionId == section.Id), merged);
                var prefix = $"{section.Code}[{i}].";
                await SubmissionDataChecks.ThrowIfBlockInvalidAsync(db, repository, sectionFields, active, normalizedMerged, prefix, cancellationToken);
                await CheckProviderChoicesAsync(form, sectionFields, merged, prefix, cancellationToken);
                normalizedBlocks.Add(sectionFields.ToDictionary(f => f.Code, f => normalizedMerged.GetValueOrDefault(f.Code), StringComparer.Ordinal));
            }
            prepared.Add(new PreparedSection(section, sectionFields, normalizedBlocks));
        }

        return new PreparedSubmission(form, version, headerFields, normalizedHeader, prepared);
    }

    // The header's lookup and uniqueness checks are the original ones; the caller passes the record id for updates.
    public Guid? ExcludeRecordId { get; set; }

    private Task SubmissionDataChecksForHeaderAsync(
        FormDefinition form, IReadOnlyCollection<FieldDefinition> headerFields, IReadOnlyDictionary<string, object?> values, CancellationToken ct) =>
        SubmissionDataChecks.ThrowIfInvalidAsync(db, repository, form.TableName!, headerFields, values, ExcludeRecordId, ct);

    private static void Collect(List<ValidationFailure> errors, string prefix, IDictionary<string, string[]> found)
    {
        foreach (var (code, messages) in found)
            errors.AddRange(messages.Select(m => new ValidationFailure(prefix + code, m)));
    }

    private static Dictionary<string, object?> Merge(IReadOnlyDictionary<string, object?> header, IReadOnlyDictionary<string, object?> block)
    {
        var merged = new Dictionary<string, object?>(header, StringComparer.Ordinal);
        foreach (var (k, v) in block) merged[k] = v;
        return merged;
    }

    private static List<Dictionary<string, object?>> ReadBlocks(object? raw, out bool malformed)
    {
        malformed = false;
        var blocks = new List<Dictionary<string, object?>>();
        if (raw is null || raw is JsonElement { ValueKind: JsonValueKind.Null or JsonValueKind.Undefined }) return blocks;
        if (raw is IEnumerable<IReadOnlyDictionary<string, object?>> already)
            return already.Select(b => new Dictionary<string, object?>(b, StringComparer.Ordinal)).ToList();
        if (raw is not JsonElement { ValueKind: JsonValueKind.Array } array) { malformed = true; return blocks; }
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) { malformed = true; return []; }
            blocks.Add(item.EnumerateObject().ToDictionary(p => p.Name, p => (object?)p.Value.Clone(), StringComparer.Ordinal));
        }
        return blocks;
    }

    /// <summary>The submitted values as text, the way providers and filters compare them.</summary>
    public static IReadOnlyDictionary<string, string?> AsText(IReadOnlyDictionary<string, object?> values) =>
        values.ToDictionary(kv => kv.Key, kv => Text(kv.Value), StringComparer.Ordinal);

    private static string? Text(object? raw) => raw switch
    {
        null => null,
        string s => s,
        bool b => b ? "true" : "false",
        Guid g => g.ToString(),
        JsonElement { ValueKind: JsonValueKind.String } je => je.GetString(),
        JsonElement { ValueKind: JsonValueKind.True } => "true",
        JsonElement { ValueKind: JsonValueKind.False } => "false",
        JsonElement { ValueKind: JsonValueKind.Number } je => je.GetRawText(),
        JsonElement => null,
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => null,
    };

    /// <summary>
    /// For every field shown by an attribute of another field's choice, looks that attribute up:
    /// from the controlling field's provider options, or from the chosen row of its Lookup.
    /// </summary>
    public async Task<Func<string, string, string?>> ResolveAttributesAsync(
        FormDefinition form, IReadOnlyCollection<FieldDefinition> fields, IReadOnlyDictionary<string, object?> values, CancellationToken ct)
    {
        var needed = fields
            .Where(f => !string.IsNullOrWhiteSpace(f.VisibleWhenFieldCode) && !string.IsNullOrWhiteSpace(f.VisibleWhenAttribute))
            .Select(f => (Controller: f.VisibleWhenFieldCode!, Attribute: f.VisibleWhenAttribute!))
            .Distinct()
            .ToList();
        var found = new Dictionary<(string, string), string?>();
        foreach (var group in needed.GroupBy(n => n.Controller))
        {
            var controller = fields.FirstOrDefault(f => f.Code == group.Key);
            var chosen = Text(values.GetValueOrDefault(group.Key));
            if (controller is null || chosen is null) continue;
            var attributes = await AttributesOfChoiceAsync(form, controller, chosen, values, group.Select(g => g.Attribute).ToList(), ct);
            foreach (var (_, attribute) in group) found[(group.Key, attribute)] = attributes.GetValueOrDefault(attribute);
        }
        return (controller, attribute) => found.GetValueOrDefault((controller, attribute));
    }

    private async Task<IReadOnlyDictionary<string, string>> AttributesOfChoiceAsync(
        FormDefinition form, FieldDefinition controller, string chosen, IReadOnlyDictionary<string, object?> values,
        IReadOnlyList<string> attributes, CancellationToken ct)
    {
        if (providers.Find(controller.OptionsProvider) is { } provider)
        {
            var options = await provider.GetOptionsAsync(new FieldOptionsContext(form, controller, AsText(values)), ct);
            return options.FirstOrDefault(o => o.Value == chosen)?.Attributes ?? new Dictionary<string, string>();
        }
        if (controller.FieldType == FieldType.Lookup && controller.LookupFormDefinitionId is { } targetId && Guid.TryParse(chosen, out var rowId))
        {
            var target = await db.FormDefinitions.Include(f => f.Versions).ThenInclude(v => v.Fields)
            .Include(f => f.Versions).ThenInclude(v => v.Sections)
                .SingleOrDefaultAsync(f => f.Id == targetId, ct);
            var targetFields = target?.GetPublishedVersion()?.Fields.Where(f => f.IsActive && attributes.Contains(f.Code)).ToList();
            if (target?.TableName is null || targetFields is null || targetFields.Count == 0) return new Dictionary<string, string>();
            var row = await repository.GetByIdAsync(target.TableName, targetFields, rowId, ct);
            return row?.Values.Where(kv => Text(kv.Value) is not null).ToDictionary(kv => kv.Key, kv => Text(kv.Value)!)
                ?? new Dictionary<string, string>();
        }
        return new Dictionary<string, string>();
    }

    public const string BlockedCode = "submission.blocked";

    /// <summary>A choice field's block-when rule: the chosen option's attribute must not be one of the blocking values.</summary>
    private async Task CheckBlockedChoicesAsync(
        FormDefinition form, IReadOnlyCollection<FieldDefinition> all, IReadOnlyCollection<FieldDefinition> fields,
        IReadOnlyDictionary<string, object?> values, string prefix, CancellationToken ct)
    {
        foreach (var field in fields)
        {
            var rules = SubmissionValueValidator.ParseRules(field.ValidationRulesJson);
            if (rules?.BlockWhenAttribute is not { Length: > 0 } attribute || rules.BlockWhenValues is not { Length: > 0 } blocking) continue;
            if (Text(values.GetValueOrDefault(field.Code)) is not { } chosen) continue;
            var attributes = await AttributesOfChoiceAsync(form, field, chosen, values, [attribute], ct);
            if (attributes.GetValueOrDefault(attribute) is { } value && blocking.Contains(value, StringComparer.Ordinal))
                throw new Common.Exceptions.ValidationException(BlockedCode,
                    [new ValidationFailure(prefix + field.Code, rules.BlockMessageEn ?? rules.BlockMessage ?? $"'{field.Label}' can't be chosen right now.")]);
        }
    }

    /// <summary>A provider-backed field accepts only what its provider offers for these values.</summary>
    private async Task CheckProviderChoicesAsync(
        FormDefinition form, IReadOnlyCollection<FieldDefinition> fields, IReadOnlyDictionary<string, object?> values, string prefix,
        CancellationToken ct)
    {
        foreach (var field in fields.Where(f => f.OptionsProvider is not null))
        {
            var picked = values.GetValueOrDefault(field.Code) switch
            {
                null => [],
                var raw when SubmissionValueValidator.TryGetStrings(raw, out var many) => many.Where(t => !string.IsNullOrWhiteSpace(t)).ToList(),
                var raw => Text(raw) is { Length: > 0 } one ? [one] : new List<string>(),
            };
            if (picked.Count == 0) continue;
            var provider = providers.Find(field.OptionsProvider)
                ?? throw new InvalidOperationException($"Options provider '{field.OptionsProvider}' isn't registered.");
            var options = await provider.GetOptionsAsync(new FieldOptionsContext(form, field, AsText(values)), ct);
            var allowed = options.Select(o => o.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (picked.Any(p => !allowed.Contains(p.Trim())))
                throw new Common.Exceptions.ValidationException(SubmissionDataChecks.ChoiceNotAllowedCode,
                    [new ValidationFailure(prefix + field.Code, $"'{field.Label}' can't be chosen here.")]);
        }
    }
}
