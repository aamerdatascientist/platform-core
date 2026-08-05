using MediatR;
using Microsoft.EntityFrameworkCore;
using Platform.Application.Common.Exceptions;
using Platform.Application.Common.Interfaces;
using Platform.Application.Forms;
using Platform.Application.Forms.Dtos;
using Platform.Domain.Forms;
using Platform.Domain.Forms.Enums;

namespace Platform.Application.Forms.Queries.GetFormDefinition;

public record GetFormDefinitionQuery(Guid FormDefinitionId) : IRequest<FormDefinitionDto>;

public class GetFormDefinitionQueryHandler : IRequestHandler<GetFormDefinitionQuery, FormDefinitionDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public GetFormDefinitionQueryHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<FormDefinitionDto> Handle(GetFormDefinitionQuery request, CancellationToken cancellationToken)
    {
        var formDefinition = await _db.FormDefinitions
            .Include(f => f.Versions).ThenInclude(v => v.Fields)
            .Include(f => f.AllowedRoles)
            .Include(f => f.AllowedUsers)
            .SingleOrDefaultAsync(f => f.Id == request.FormDefinitionId, cancellationToken);

        if (formDefinition is null)
            throw new NotFoundException(nameof(FormDefinition), request.FormDefinitionId);

        var roleNamesById = await _db.Roles.ToDictionaryAsync(r => r.Id, r => r.Name, cancellationToken);
        var allowedNames = formDefinition.AllowedRoles
            .Select(ar => roleNamesById.GetValueOrDefault(ar.RoleId)).Where(n => n is not null).Select(n => n!).ToList();
        if (!FormAccessChecker.HasAccess(
                allowedNames,
                formDefinition.AllowedUsers.Select(au => au.UserId).ToList(),
                _currentUser.Roles,
                _currentUser.UserId))
            throw new ForbiddenAccessException($"You don't have access to form '{formDefinition.Name}'.");

        var draft = formDefinition.Versions.SingleOrDefault(v => v.Status == FormStatus.Draft);
        var published = formDefinition.GetPublishedVersion();

        return new FormDefinitionDto(
            formDefinition.Id, formDefinition.Code, formDefinition.Name, formDefinition.Description,
            formDefinition.ModuleName, formDefinition.Status, formDefinition.TableName,
            ToDto(draft), ToDto(published), formDefinition.AllowedRoles.Select(ar => ar.RoleId).ToList(),
            formDefinition.AllowedUsers.Select(au => au.UserId).ToList());
    }

    private static FormVersionDto? ToDto(FormVersion? version)
    {
        if (version is null) return null;

        var fields = version.Fields
            .OrderBy(f => f.DisplayOrder)
            .Select(f => new FieldDefinitionDto(
                f.Id, f.Code, f.Label, f.FieldType, f.IsRequired, f.IsActive,
                f.DisplayOrder, f.OptionsJson, f.LookupFormDefinitionId, f.ValidationRulesJson))
            .ToList();

        return new FormVersionDto(version.Id, version.VersionNumber, version.Status, version.PublishedAtUtc, fields);
    }
}
