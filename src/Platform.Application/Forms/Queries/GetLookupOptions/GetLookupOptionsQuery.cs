using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Platform.Application.Common.Exceptions;
using Platform.Application.Common.Interfaces;
using Platform.Domain.Forms;
using Platform.Domain.Forms.Enums;

namespace Platform.Application.Forms.Queries.GetLookupOptions;

public record LookupOptionDto(Guid Id, string Label);

/// <summary>
/// The choices for one Lookup field on a form being filled in - each row of the target form
/// reduced to its Id and a display label, and nothing else.
///
/// Access is checked against the form being FILLED (FormDefinitionId, via
/// IFormScopedRequest), not against the Lookup's target form. That is the point of this
/// query: someone allowed to fill in the Daily Progress Report must be able to pick a
/// project from its Project field even when the Projects form itself is closed to them -
/// and they get only the choice labels, never the target form's other columns. Before this
/// existed, the form loaded its choices from the target form's full record list, so locking
/// a master-data form also emptied every dropdown that pointed at it.
///
/// The label is the target form's first active ShortText field in display order - the same
/// convention the frontend used before (there is still no designated display field, see
/// CLAUDE.md's known gaps). A filtered (cascading) Lookup - FieldDefinition.FilterByFieldCode,
/// e.g. Zone narrowed by Project - needs <paramref name="FilterValue"/>: the value currently
/// picked in the sibling field, matched against the target form's field of the same code.
/// Without it the answer is empty, as the form shows nothing until the sibling is picked.
/// </summary>
public record GetLookupOptionsQuery(Guid FormDefinitionId, Guid FieldDefinitionId, string? FilterValue = null)
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

    /// <summary>A dropdown longer than this is unusable anyway; stops a runaway read.</summary>
    private const int MaxOptions = 2000;

    private readonly IApplicationDbContext _db;
    private readonly IDynamicDataRepository _dynamicDataRepository;

    public GetLookupOptionsQueryHandler(IApplicationDbContext db, IDynamicDataRepository dynamicDataRepository)
    {
        _db = db;
        _dynamicDataRepository = dynamicDataRepository;
    }

    public async Task<IReadOnlyList<LookupOptionDto>> Handle(GetLookupOptionsQuery request, CancellationToken cancellationToken)
    {
        var formDefinition = await _db.FormDefinitions
            .Include(f => f.Versions).ThenInclude(v => v.Fields)
            .SingleOrDefaultAsync(f => f.Id == request.FormDefinitionId, cancellationToken);

        if (formDefinition is null)
            throw new NotFoundException(nameof(FormDefinition), request.FormDefinitionId);

        // Every "not actually a usable Lookup right now" shape answers with no choices rather
        // than an error, same as GetFieldDynamicOptionsQuery: the form still renders.
        var field = formDefinition.GetPublishedVersion()?.Fields
            .SingleOrDefault(f => f.Id == request.FieldDefinitionId && f.IsActive);
        if (field is null || field.FieldType != FieldType.Lookup || field.LookupFormDefinitionId is null)
            return Array.Empty<LookupOptionDto>();

        var targetForm = await _db.FormDefinitions
            .Include(f => f.Versions).ThenInclude(v => v.Fields)
            .SingleOrDefaultAsync(f => f.Id == field.LookupFormDefinitionId, cancellationToken);
        var targetVersion = targetForm?.GetPublishedVersion();
        if (targetForm?.TableName is null || targetVersion is null)
            return Array.Empty<LookupOptionDto>();

        var targetFields = targetVersion.Fields.Where(f => f.IsActive).ToList();
        var displayField = targetFields
            .Where(f => f.FieldType == FieldType.ShortText)
            .OrderBy(f => f.DisplayOrder)
            .FirstOrDefault();

        DynamicRowFilter? filter = null;
        if (!string.IsNullOrWhiteSpace(field.FilterByFieldCode))
        {
            var filterField = targetFields.SingleOrDefault(f =>
                string.Equals(f.Code, field.FilterByFieldCode, StringComparison.OrdinalIgnoreCase));
            if (filterField is null || filterField.FieldType != FieldType.Lookup ||
                !Guid.TryParse(request.FilterValue, out var filterId))
                return Array.Empty<LookupOptionDto>();
            filter = new DynamicRowFilter(filterField.Code, filterId);
        }

        var options = new List<LookupOptionDto>();
        for (var page = 1; options.Count < MaxOptions; page++)
        {
            var result = await _dynamicDataRepository.QueryAsync(
                targetForm.TableName, targetFields, page, PageSize, filter, cancellationToken);

            options.AddRange(result.Items.Select(row => new LookupOptionDto(
                row.Id,
                (displayField is null ? null : row.Values.GetValueOrDefault(displayField.Code)?.ToString()) is { Length: > 0 } label
                    ? label
                    : row.Id.ToString())));

            if (result.Items.Count == 0 || page * PageSize >= result.TotalCount) break;
        }

        return options.Count > MaxOptions ? options.Take(MaxOptions).ToList() : options;
    }
}
