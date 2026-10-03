using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Platform.Application.Common.Exceptions;
using Platform.Application.Common.Interfaces;
using Platform.Domain.Forms;

namespace Platform.Application.Forms.Queries.GetFieldDynamicOptions;

/// <summary>
/// The live option list for a Dropdown field whose options come from another form's field
/// (see FieldDefinition.DynamicOptionsSourceFormDefinitionId/Code) - the distinct, non-blank
/// values currently submitted for that source field, read fresh on every call rather than
/// cached, since the whole point is staying in sync with the source form's submissions
/// automatically. FieldDefinitionId names the field being RENDERED (e.g. Outflow's
/// "material"), not the source field - the source form/field are read off that field's own
/// metadata, the same way a Lookup field's candidates are resolved from its own
/// LookupFormDefinitionId rather than from a parameter the frontend has to already know.
/// </summary>
public record GetFieldDynamicOptionsQuery(Guid FormDefinitionId, Guid FieldDefinitionId)
    : IRequest<IReadOnlyList<string>>;

public class GetFieldDynamicOptionsQueryValidator : AbstractValidator<GetFieldDynamicOptionsQuery>
{
    public GetFieldDynamicOptionsQueryValidator()
    {
        RuleFor(x => x.FormDefinitionId).NotEmpty();
        RuleFor(x => x.FieldDefinitionId).NotEmpty();
    }
}

public class GetFieldDynamicOptionsQueryHandler : IRequestHandler<GetFieldDynamicOptionsQuery, IReadOnlyList<string>>
{
    private readonly IApplicationDbContext _db;
    private readonly IDynamicDataRepository _dynamicDataRepository;

    public GetFieldDynamicOptionsQueryHandler(IApplicationDbContext db, IDynamicDataRepository dynamicDataRepository)
    {
        _db = db;
        _dynamicDataRepository = dynamicDataRepository;
    }

    public async Task<IReadOnlyList<string>> Handle(
        GetFieldDynamicOptionsQuery request, CancellationToken cancellationToken)
    {
        var formDefinition = await _db.FormDefinitions
            .Include(f => f.Versions).ThenInclude(v => v.Fields)
            .SingleOrDefaultAsync(f => f.Id == request.FormDefinitionId, cancellationToken);

        if (formDefinition is null)
            throw new NotFoundException(nameof(FormDefinition), request.FormDefinitionId);

        var publishedVersion = formDefinition.GetPublishedVersion();
        var field = publishedVersion?.Fields.SingleOrDefault(f => f.Id == request.FieldDefinitionId);

        // Fails open to an empty list rather than an error for every shape of "not actually a
        // dynamic dropdown right now" (field not found, form not published, no source
        // configured) - same philosophy as SubmissionValueValidator's own fail-open cases.
        // The frontend only ever calls this for a field it has already seen carries a dynamic
        // options source, so these branches are defensive, not the expected path.
        if (field is null || field.DynamicOptionsSourceFormDefinitionId is null ||
            string.IsNullOrWhiteSpace(field.DynamicOptionsSourceFieldCode))
            return Array.Empty<string>();

        var sourceForm = await _db.FormDefinitions
            .SingleOrDefaultAsync(f => f.Id == field.DynamicOptionsSourceFormDefinitionId, cancellationToken);

        if (sourceForm?.TableName is null)
            return Array.Empty<string>();

        return await _dynamicDataRepository.GetDistinctColumnValuesAsync(
            sourceForm.TableName, field.DynamicOptionsSourceFieldCode, cancellationToken);
    }
}
