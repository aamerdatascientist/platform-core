using System.Globalization;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Platform.Application.Common.Exceptions;
using Platform.Application.Common.Interfaces;
using Platform.Domain.Forms;
using Platform.Domain.Forms.Enums;

namespace Platform.Application.Forms.Records;

/// <summary>
/// The Records tab (screen 20, HANDOFF §9.4): a form's submitted records, newest first, with
/// server-side filters - free text over the text answers, a date range (on a DateTime field,
/// or on when it was sent), and any number of equality filters ("code:value", e.g. project,
/// phase, had_delay:true). Administrators only (the endpoint says so; FormAccessBehavior still
/// checks the form). Each record carries its sender, time, Lookup labels and attachment count.
/// </summary>
public record SearchRecordsQuery(
    Guid FormDefinitionId,
    int Page = 1,
    int PageSize = 25,
    string? Q = null,
    string? From = null,
    string? To = null,
    string? DateField = null,
    IReadOnlyList<string>? Filters = null) : IRequest<RecordsPageDto>, IFormScopedRequest;

public class SearchRecordsQueryValidator : AbstractValidator<SearchRecordsQuery>
{
    public SearchRecordsQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 200);
        RuleFor(x => x.Q).MaximumLength(200);
        RuleForEach(x => x.Filters).Must(f => f.Contains(':')).WithMessage("A filter is 'code:value'.");
        RuleFor(x => x.Filters).Must(f => f is null || f.Count <= 10).WithMessage("At most 10 filters.");
    }
}

public class SearchRecordsQueryHandler : IRequestHandler<SearchRecordsQuery, RecordsPageDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IDynamicDataRepository _repository;
    private readonly IFieldOptionsProviders _providers;

    public SearchRecordsQueryHandler(IApplicationDbContext db, IDynamicDataRepository repository, IFieldOptionsProviders providers)
    {
        _providers = providers;
        _db = db;
        _repository = repository;
    }

    public async Task<RecordsPageDto> Handle(SearchRecordsQuery request, CancellationToken cancellationToken)
    {
        var form = await _db.FormDefinitions.Include(f => f.Versions).ThenInclude(v => v.Fields)
        .Include(f => f.Versions).ThenInclude(v => v.Sections)
            .SingleOrDefaultAsync(f => f.Id == request.FormDefinitionId, cancellationToken)
            ?? throw new NotFoundException(nameof(FormDefinition), request.FormDefinitionId);
        var version = form.GetPublishedVersion();
        if (version is null || form.TableName is null)
            return new RecordsPageDto(Array.Empty<RecordItemDto>(), 0, request.Page, request.PageSize);
        var fields = version.Fields.Where(f => f.IsActive).ToList();

        var matches = new List<DynamicRowFilter>();
        foreach (var raw in request.Filters ?? Array.Empty<string>())
        {
            var at = raw.IndexOf(':');
            var code = raw[..at];
            var field = fields.SingleOrDefault(f => string.Equals(f.Code, code, StringComparison.OrdinalIgnoreCase) && f.FieldType != FieldType.Attachment)
                ?? throw FormEditLoading.Invalid(nameof(request.Filters), $"'{code}' isn't a filterable field on this form.");
            matches.Add(new DynamicRowFilter(field.Code, RecordFilterValues.Convert(field, raw[(at + 1)..], nameof(request.Filters))));
        }

        string? dateField = null;
        if (!string.IsNullOrWhiteSpace(request.DateField))
        {
            dateField = fields.SingleOrDefault(f => string.Equals(f.Code, request.DateField, StringComparison.OrdinalIgnoreCase) && f.FieldType == FieldType.DateTime)?.Code
                ?? throw FormEditLoading.Invalid(nameof(request.DateField), $"'{request.DateField}' isn't a date field on this form.");
        }

        var page = await _repository.SearchAsync(form.TableName, fields, new RecordSearch(
            request.Page, request.PageSize, matches, request.Q,
            dateField, RecordFilterValues.SaudiDayStart(request.From, nameof(request.From)), RecordFilterValues.SaudiDayStart(request.To, nameof(request.To), nextDay: true)),
            cancellationToken);
        var items = await RecordViews.ToItemsAsync(_db, _repository, form.Id, fields, page.Items, cancellationToken, _providers);
        return new RecordsPageDto(items, page.TotalCount, page.Page, page.PageSize);
    }
}

public static class RecordFilterValues
{
    private static readonly TimeSpan Saudi = TimeSpan.FromHours(3);

    /// <summary>A query-string value in the field's real type (as GetFormSubmissions does); 400 if it isn't one.</summary>
    public static object Convert(FieldDefinition field, string raw, string property)
    {
        try
        {
            return field.FieldType switch
            {
                FieldType.ShortText or FieldType.LongText or FieldType.Dropdown => raw,
                FieldType.Number => int.Parse(raw, CultureInfo.InvariantCulture),
                FieldType.Decimal => decimal.Parse(raw, CultureInfo.InvariantCulture),
                FieldType.Boolean => bool.Parse(raw),
                FieldType.DateTime => DateTimeOffset.Parse(raw, CultureInfo.InvariantCulture).ToUniversalTime(),
                FieldType.Lookup => Guid.Parse(raw),
                _ => throw new FormatException(),
            };
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException or OverflowException)
        {
            throw FormEditLoading.Invalid(property, $"'{raw}' isn't a valid value for '{field.Code}'.");
        }
    }

    /// <summary>"2026-11-07" -> the start of that Saudi day (or of the next one, for an exclusive end).</summary>
    public static DateTimeOffset? SaudiDayStart(string? day, string property, bool nextDay = false)
    {
        if (string.IsNullOrWhiteSpace(day)) return null;
        if (!DateOnly.TryParseExact(day, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            throw FormEditLoading.Invalid(property, "Dates are yyyy-mm-dd.");
        if (nextDay) date = date.AddDays(1);
        return new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), Saudi);
    }
}
