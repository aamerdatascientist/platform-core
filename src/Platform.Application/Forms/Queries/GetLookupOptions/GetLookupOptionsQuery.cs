using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Platform.Application.Common.Exceptions;
using Platform.Application.Common.Interfaces;
using Platform.Domain.Forms;
using Platform.Domain.Forms.Enums;

namespace Platform.Application.Forms.Queries.GetLookupOptions;

/// <summary>
/// One option of a choice field. <paramref name="Id"/> is what gets submitted - a row id for a
/// Lookup, the option value for a provider-backed field. <paramref name="Attributes"/> are the
/// option's settings other fields' visibility depends on (only those this form uses).
/// </summary>
public record LookupOptionDto(string Id, string Label, string? LabelEn = null, IReadOnlyDictionary<string, string>? Attributes = null);

/// <summary>
/// The choices for one field of a form, checked against access to THIS form (not the Lookup's
/// target), so a form can be filled in by people who can't open the master-data forms behind
/// it - project managers among them. Serves:
/// - Lookup / MultiLookup: the target form's rows, narrowed by the cascade filter
///   (FilterByFieldCode, value given as <paramref name="FilterValue"/> or in Values) and the
///   row filter (RowFilterJson);
/// - any choice field with an options provider: the provider's options for the current values.
/// </summary>
public record GetLookupOptionsQuery(
    Guid FormDefinitionId, Guid FieldDefinitionId, string? FilterValue = null, IReadOnlyDictionary<string, string?>? Values = null)
    : IRequest<IReadOnlyList<LookupOptionDto>>, IFormScopedRequest;

public class GetLookupOptionsQueryValidator : AbstractValidator<GetLookupOptionsQuery>
{
    public GetLookupOptionsQueryValidator()
    {
        RuleFor(x => x.FormDefinitionId).NotEmpty();
        RuleFor(x => x.FieldDefinitionId).NotEmpty();
    }
}

public class GetLookupOptionsQueryHandler : IRequestHandler<GetLookupOptionsQuery, IReadOnlyList<LookupOptionDto>>
{
    private const int PageSize = 200;
    private const int MaxOptions = 2000;

    private readonly IApplicationDbContext _db;
    private readonly IDynamicDataRepository _dynamicDataRepository;
    private readonly IFieldOptionsProviders _providers;

    public GetLookupOptionsQueryHandler(IApplicationDbContext db, IDynamicDataRepository dynamicDataRepository, IFieldOptionsProviders providers)
    {
        _db = db;
        _dynamicDataRepository = dynamicDataRepository;
        _providers = providers;
    }

    public async Task<IReadOnlyList<LookupOptionDto>> Handle(GetLookupOptionsQuery request, CancellationToken cancellationToken)
    {
        var formDefinition = await _db.FormDefinitions
            .Include(f => f.Versions).ThenInclude(v => v.Fields)
            .Include(f => f.Versions).ThenInclude(v => v.Sections)
            .SingleOrDefaultAsync(f => f.Id == request.FormDefinitionId, cancellationToken);
        if (formDefinition is null)
            throw new NotFoundException(nameof(FormDefinition), request.FormDefinitionId);

        var version = formDefinition.GetPublishedVersion();
        var field = version?.Fields.SingleOrDefault(f => f.Id == request.FieldDefinitionId && f.IsActive);
        if (version is null || field is null) return Array.Empty<LookupOptionDto>();

        var values = new Dictionary<string, string?>(request.Values ?? new Dictionary<string, string?>(), StringComparer.Ordinal);
        if (field.FilterByFieldCode is { } filterCode && request.FilterValue is { } fv) values[filterCode] = fv;

        if (_providers.Find(field.OptionsProvider) is { } provider)
        {
            var provided = await provider.GetOptionsAsync(new FieldOptionsContext(formDefinition, field, values), cancellationToken);
            return provided.Take(MaxOptions).Select(o => new LookupOptionDto(o.Value, o.Label, o.LabelEn, o.Attributes)).ToList();
        }

        if (field.FieldType is not (FieldType.Lookup or FieldType.MultiLookup) || field.LookupFormDefinitionId is null)
            return Array.Empty<LookupOptionDto>();

        var targetForm = await _db.FormDefinitions
            .Include(f => f.Versions).ThenInclude(v => v.Fields)
            .SingleOrDefaultAsync(f => f.Id == field.LookupFormDefinitionId, cancellationToken);
        var targetVersion = targetForm?.GetPublishedVersion();
        if (targetForm?.TableName is null || targetVersion is null)
            return Array.Empty<LookupOptionDto>();

        var targetFields = targetVersion.Fields.Where(f => f.IsActive && f.SectionId is null).ToList();
        var displayField = targetFields
            .Where(f => f.FieldType == FieldType.ShortText)
            .OrderBy(f => f.DisplayOrder)
            .FirstOrDefault();
        // English display: a ShortText field coded label_en on the target, when it has one.
        var displayFieldEn = targetFields.FirstOrDefault(f => f.Code == "label_en" && f.FieldType == FieldType.ShortText);

        DynamicRowFilter? filter = null;
        if (!string.IsNullOrWhiteSpace(field.FilterByFieldCode))
        {
            var filterField = targetFields.SingleOrDefault(f =>
                string.Equals(f.Code, field.FilterByFieldCode, StringComparison.OrdinalIgnoreCase));
            if (filterField is null || filterField.FieldType != FieldType.Lookup ||
                !Guid.TryParse(values.GetValueOrDefault(field.FilterByFieldCode), out var filterId))
                return Array.Empty<LookupOptionDto>();
            filter = new DynamicRowFilter(filterField.Code, filterId);
        }

        var rowFilter = RowFilter.Parse(field.RowFilterJson);
        var attributeCodes = version.Fields
            .Where(f => f.IsActive && f.VisibleWhenFieldCode == field.Code && !string.IsNullOrWhiteSpace(f.VisibleWhenAttribute))
            .Select(f => f.VisibleWhenAttribute!)
            .Where(code => targetFields.Any(t => t.Code == code))
            .Distinct()
            .ToList();

        var options = new List<LookupOptionDto>();
        for (var page = 1; options.Count < MaxOptions; page++)
        {
            var result = await _dynamicDataRepository.QueryAsync(
                targetForm.TableName, targetFields, page, PageSize, filter, cancellationToken);
            foreach (var row in result.Items)
            {
                if (rowFilter is not null && !rowFilter.Allows(row.Values.GetValueOrDefault(rowFilter.Field))) continue;
                var label = (displayField is null ? null : row.Values.GetValueOrDefault(displayField.Code)?.ToString()) is { Length: > 0 } text
                    ? text
                    : row.Id.ToString();
                var labelEn = displayFieldEn is null ? null : row.Values.GetValueOrDefault(displayFieldEn.Code)?.ToString();
                var attributes = attributeCodes.Count == 0 ? null : attributeCodes
                    .Where(code => SubmissionPreparer.AsText(row.Values).GetValueOrDefault(code) is not null)
                    .ToDictionary(code => code, code => SubmissionPreparer.AsText(row.Values)[code]!);
                options.Add(new LookupOptionDto(row.Id.ToString(), label, string.IsNullOrWhiteSpace(labelEn) ? null : labelEn, attributes));
            }
            if (result.Items.Count == 0 || page * PageSize >= result.TotalCount) break;
        }

        return options.Count > MaxOptions ? options.Take(MaxOptions).ToList() : options;
    }
}
