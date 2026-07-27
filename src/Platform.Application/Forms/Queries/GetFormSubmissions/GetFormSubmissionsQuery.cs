using MediatR;
using Microsoft.EntityFrameworkCore;
using Platform.Application.Common.Exceptions;
using Platform.Application.Common.Interfaces;
using Platform.Domain.Forms;

namespace Platform.Application.Forms.Queries.GetFormSubmissions;

public record GetFormSubmissionsQuery(Guid FormDefinitionId, int Page = 1, int PageSize = 25)
    : IRequest<PagedResult<DynamicRow>>;

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

        return await _dynamicDataRepository.QueryAsync(
            formDefinition.TableName, activeFields, request.Page, request.PageSize, cancellationToken);
    }
}
