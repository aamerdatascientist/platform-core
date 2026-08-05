using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Platform.Application.Common.Exceptions;
using Platform.Application.Common.Interfaces;

namespace Platform.Application.Forms.Commands.SubmitFormData;

public record SubmitFormDataCommand(
    Guid FormDefinitionId, IReadOnlyDictionary<string, object?> Values, Guid SubmittedByUserId) : IRequest<Guid>;

public class SubmitFormDataCommandValidator : AbstractValidator<SubmitFormDataCommand>
{
    public SubmitFormDataCommandValidator()
    {
        RuleFor(x => x.FormDefinitionId).NotEmpty();
        RuleFor(x => x.SubmittedByUserId).NotEmpty();
    }
}

public class SubmitFormDataCommandHandler : IRequestHandler<SubmitFormDataCommand, Guid>
{
    private readonly IApplicationDbContext _db;
    private readonly IDynamicDataRepository _dynamicDataRepository;
    private readonly ICurrentUserService _currentUser;

    public SubmitFormDataCommandHandler(
        IApplicationDbContext db, IDynamicDataRepository dynamicDataRepository, ICurrentUserService currentUser)
    {
        _db = db;
        _dynamicDataRepository = dynamicDataRepository;
        _currentUser = currentUser;
    }

    public async Task<Guid> Handle(SubmitFormDataCommand request, CancellationToken cancellationToken)
    {
        var formDefinition = await _db.FormDefinitions
            .Include(f => f.Versions).ThenInclude(v => v.Fields)
            .Include(f => f.AllowedRoles)
            .Include(f => f.AllowedUsers)
            .SingleOrDefaultAsync(f => f.Id == request.FormDefinitionId, cancellationToken);

        if (formDefinition is null)
            throw new NotFoundException(nameof(Platform.Domain.Forms.FormDefinition), request.FormDefinitionId);

        var roleNamesById = await _db.Roles.ToDictionaryAsync(r => r.Id, r => r.Name, cancellationToken);
        var allowedNames = formDefinition.AllowedRoles
            .Select(ar => roleNamesById.GetValueOrDefault(ar.RoleId)).Where(n => n is not null).Select(n => n!).ToList();
        if (!FormAccessChecker.HasAccess(
                allowedNames,
                formDefinition.AllowedUsers.Select(au => au.UserId).ToList(),
                _currentUser.Roles,
                _currentUser.UserId))
            throw new ForbiddenAccessException($"You don't have access to form '{formDefinition.Name}'.");

        var publishedVersion = formDefinition.GetPublishedVersion();
        if (publishedVersion is null || formDefinition.TableName is null)
            throw new Platform.Application.Common.Exceptions.ValidationException(new[]
            {
                new FluentValidation.Results.ValidationFailure(
                    nameof(request.FormDefinitionId), "This form has no published version to submit data against.")
            });

        var activeFields = publishedVersion.Fields.Where(f => f.IsActive).ToList();

        var validationErrors = SubmissionValueValidator.Validate(activeFields, request.Values);
        if (validationErrors.Count != 0)
            throw new Platform.Application.Common.Exceptions.ValidationException(validationErrors.SelectMany(kv =>
                kv.Value.Select(msg => new FluentValidation.Results.ValidationFailure(kv.Key, msg))));

        var newRecordId = await _dynamicDataRepository.InsertAsync(
            formDefinition.TableName, activeFields, request.Values, request.SubmittedByUserId, cancellationToken);

        var publishedWorkflow = await _db.WorkflowDefinitions
            .Include(w => w.States)
            .SingleOrDefaultAsync(
                w => w.FormDefinitionId == formDefinition.Id && w.Status == Domain.Workflow.WorkflowStatus.Published,
                cancellationToken);

        if (publishedWorkflow is not null)
        {
            var initialState = publishedWorkflow.GetInitialState();
            var instance = Domain.Workflow.WorkflowInstance.Start(
                publishedWorkflow.Id, formDefinition.Id, newRecordId, initialState.Id, request.SubmittedByUserId);
            _db.WorkflowInstances.Add(instance);
            await _db.SaveChangesAsync(cancellationToken);
        }

        return newRecordId;
    }
}
