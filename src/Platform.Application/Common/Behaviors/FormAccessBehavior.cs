using MediatR;
using Microsoft.EntityFrameworkCore;
using Platform.Application.Common.Exceptions;
using Platform.Application.Common.Interfaces;
using Platform.Application.Forms;

namespace Platform.Application.Common.Behaviors;

/// <summary>
/// Enforces IFormScopedRequest/IFormScopeResolvingRequest automatically, so access-checking
/// a form-scoped request is something a handler can't forget to do - see both interfaces'
/// own doc comments. Every other request type passes through untouched.
///
/// Mirrors the exact check SubmitFormDataCommandHandler already does inline (load
/// AllowedRoles/AllowedUsers, resolve role names, call FormAccessChecker.HasAccess) - this
/// doesn't change that handler, since it isn't form-scoped through this mechanism and
/// already has its own correct check; it exists to give every OTHER form-touching request
/// the same guarantee without repeating that logic by hand at every call site.
/// </summary>
public class FormAccessBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public FormAccessBehavior(IApplicationDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<TResponse> Handle(
        TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        Guid? formDefinitionId = request switch
        {
            IFormScopedRequest formScoped => formScoped.FormDefinitionId,
            IFormScopeResolvingRequest resolving => await resolving.ResolveFormDefinitionIdAsync(_db, cancellationToken),
            _ => null
        };

        if (formDefinitionId is { } id)
            await EnsureAccessAsync(id, cancellationToken);

        return await next();
    }

    private async Task EnsureAccessAsync(Guid formDefinitionId, CancellationToken cancellationToken)
    {
        var formDefinition = await _db.FormDefinitions
            .Include(f => f.AllowedRoles)
            .Include(f => f.AllowedUsers)
            .SingleOrDefaultAsync(f => f.Id == formDefinitionId, cancellationToken);

        if (formDefinition is null)
            throw new NotFoundException(nameof(Domain.Forms.FormDefinition), formDefinitionId);

        var roleNamesById = await _db.Roles.ToDictionaryAsync(r => r.Id, r => r.Name, cancellationToken);
        var allowedRoleNames = formDefinition.AllowedRoles
            .Select(ar => roleNamesById.GetValueOrDefault(ar.RoleId))
            .Where(n => n is not null)
            .Select(n => n!)
            .ToList();

        if (!FormAccessChecker.HasAccess(
                allowedRoleNames,
                formDefinition.AllowedUsers.Select(au => au.UserId).ToList(),
                _currentUser.Roles,
                _currentUser.UserId))
            throw new ForbiddenAccessException($"You don't have access to form '{formDefinition.Name}'.");
    }
}
