using System.Globalization;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Platform.Application.Common.Exceptions;
using Platform.Application.Common.Interfaces;
using Platform.Domain.Forms;
using Platform.Domain.Forms.Enums;

namespace Platform.Application.Forms.Queries.GetFormSubmissions;

/// <summary>
/// FilterFieldCode/FilterValue are both optional and both-or-nothing - scoping submissions
/// by one field's value (e.g. a daily-report form's Project Lookup) without building a
/// general-purpose query language. FilterValue arrives as a string (it's bound from a query
/// string) and gets converted to the field's real CLR type in the handler before it reaches
/// the repository.
/// </summary>
public record GetFormSubmissionsQuery(
    Guid FormDefinitionId, int Page = 1, int PageSize = 25, string? FilterFieldCode = null, string? FilterValue = null)
    : IRequest<PagedResult<DynamicRow>>, IFormScopedRequest;

public class GetFormSubmissionsQueryValidator : AbstractValidator<GetFormSubmissionsQuery>
{
    public GetFormSubmissionsQueryValidator()
    {
        RuleFor(x => x.FilterFieldCode).NotEmpty().When(x => x.FilterValue is not null)
            .WithMessage("FilterFieldCode is required when FilterValue is set.");
        RuleFor(x => x.FilterValue).NotEmpty().When(x => x.FilterFieldCode is not null)
            .WithMessage("FilterValue is required when FilterFieldCode is set.");
    }
}

public class GetFormSubmissionsQueryHandler : IRequestHandler<GetFormSubmissionsQuery, PagedResult<DynamicRow>>
{
    private readonly IApplicationDbContext _db;
    private readonly IDynamicDataRepository _dynamicDataRepository;

    public GetFormSubmissionsQueryHandler(IApplicationDbContext db, IDynamicDataRepository dynamicDataRepository)
    {
        _db = db;
        _dynamicDataRepository = dynamicDataRepository;
    }

    public async Task<PagedResult<DynamicRow>> Handle(GetFormSubmissionsQuery request, CancellationToken cancellationToken)
    {
        var formDefinition = await _db.FormDefinitions
            .Include(f => f.Versions).ThenInclude(v => v.Fields)
            .SingleOrDefaultAsync(f => f.Id == request.FormDefinitionId, cancellationToken);

        if (formDefinition is null)
            throw new NotFoundException(nameof(FormDefinition), request.FormDefinitionId);

        var publishedVersion = formDefinition.GetPublishedVersion();
        if (publishedVersion is null || formDefinition.TableName is null)
            return new PagedResult<DynamicRow>(Array.Empty<DynamicRow>(), 0, request.Page, request.PageSize);

        var activeFields = publishedVersion.Fields.Where(f => f.IsActive).ToList();

        DynamicRowFilter? filter = null;
        if (request.FilterFieldCode is not null)
        {
            var filterField = activeFields.SingleOrDefault(
                f => string.Equals(f.Code, request.FilterFieldCode, StringComparison.OrdinalIgnoreCase));

            if (filterField is null || filterField.FieldType == FieldType.Attachment)
                throw new Common.Exceptions.ValidationException(new[]
                {
                    new FluentValidation.Results.ValidationFailure(
                        nameof(request.FilterFieldCode),
                        $"'{request.FilterFieldCode}' isn't a filterable active field on this form.")
                });

            try
            {
                filter = new DynamicRowFilter(filterField.Code, ConvertFilterValue(filterField.FieldType, request.FilterValue!));
            }
            catch (Exception ex) when (ex is FormatException or ArgumentException or OverflowException)
            {
                throw new Common.Exceptions.ValidationException(new[]
                {
                    new FluentValidation.Results.ValidationFailure(
                        nameof(request.FilterValue),
                        $"'{request.FilterValue}' isn't a valid value for field '{filterField.Code}'.")
                });
            }
        }

        return await _dynamicDataRepository.QueryAsync(
            formDefinition.TableName, activeFields, request.Page, request.PageSize, filter, cancellationToken);
    }

    /// <summary>Mirrors DynamicDataRepository.ConvertFieldValue's type mapping, but from a
    /// query-string value rather than a JsonElement, since a filter arrives via GET.</summary>
    private static object ConvertFilterValue(FieldType fieldType, string rawValue) => fieldType switch
    {
        FieldType.ShortText or FieldType.LongText or FieldType.Dropdown => rawValue,
        FieldType.Number => int.Parse(rawValue, CultureInfo.InvariantCulture),
        FieldType.Decimal => decimal.Parse(rawValue, CultureInfo.InvariantCulture),
        FieldType.Boolean => bool.Parse(rawValue),
        FieldType.DateTime => DateTimeOffset.Parse(rawValue, CultureInfo.InvariantCulture).ToUniversalTime(),
        FieldType.Lookup => Guid.Parse(rawValue),
        _ => throw new NotSupportedException($"Unsupported field type '{fieldType}' for filtering.")
    };
}
