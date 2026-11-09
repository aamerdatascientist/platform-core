using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Platform.Application.Common.Exceptions;
using Platform.Application.Common.Interfaces;

namespace Platform.Application.Forms.Commands.UpdateSubmission;

/// <summary>
/// Corrects a submitted record (administrators only - enforced on the endpoint). The values
/// are the whole record, exactly as a new submission would send them, and go through the
/// same SubmissionValueValidator rules, so an edit can't store anything a submission
/// couldn't. Fields not sent become empty. Attachments are not touched (they live in the
/// file store, not in the row), and a workflow already running on the record carries on
/// unchanged.
/// </summary>
public record UpdateSubmissionCommand(
    Guid FormDefinitionId, Guid RecordId, IReadOnlyDictionary<string, object?> Values, Guid ModifiedByUserId)
    : IRequest, IFormScopedRequest;

public class UpdateSubmissionCommandValidator : AbstractValidator<UpdateSubmissionCommand>
{
    public UpdateSubmissionCommandValidator()
    {
        RuleFor(x => x.FormDefinitionId).NotEmpty();
        RuleFor(x => x.RecordId).NotEmpty();
        RuleFor(x => x.ModifiedByUserId).NotEmpty();
    }
}

public class UpdateSubmissionCommandHandler : IRequestHandler<UpdateSubmissionCommand>
{
    private readonly IApplicationDbContext _db;
    private readonly IDynamicDataRepository _dynamicDataRepository;
    private readonly SubmissionPreparer _preparer;
    private readonly IFormSubmissionHooks _hooks;
    private readonly ICurrentUserService _currentUser;

    public UpdateSubmissionCommandHandler(
        IApplicationDbContext db, IDynamicDataRepository dynamicDataRepository, SubmissionPreparer preparer,
        IFormSubmissionHooks hooks, ICurrentUserService currentUser)
    {
        _db = db;
        _dynamicDataRepository = dynamicDataRepository;
        _preparer = preparer;
        _hooks = hooks;
        _currentUser = currentUser;
    }

    public async Task Handle(UpdateSubmissionCommand request, CancellationToken cancellationToken)
    {
        var formDefinition = await _db.FormDefinitions
            .Include(f => f.Versions).ThenInclude(v => v.Fields)
            .Include(f => f.Versions).ThenInclude(v => v.Sections)
            .SingleOrDefaultAsync(f => f.Id == request.FormDefinitionId, cancellationToken);

        if (formDefinition is null)
            throw new NotFoundException(nameof(Domain.Forms.FormDefinition), request.FormDefinitionId);

        var publishedVersion = formDefinition.GetPublishedVersion();
        if (publishedVersion is null || formDefinition.TableName is null)
            throw new NotFoundException("Record", request.RecordId);

        // The whole record, sections included, validated like a new submission.
        _preparer.ExcludeRecordId = request.RecordId;
        var prepared = await _preparer.PrepareAsync(formDefinition, publishedVersion, request.Values, cancellationToken);
        var hooks = _hooks.For(formDefinition.Code);
        var hookContext = new SubmissionHookContext(prepared, request.ModifiedByUserId, _currentUser.Roles, request.RecordId);
        var guard = hooks.Count == 0 ? null : new WriteGuard(
            hooks.Select(h => h.LockKey(hookContext)).FirstOrDefault(k => k is not null),
            async ct => { foreach (var hook in hooks) await hook.ValidateAsync(hookContext, ct); });

        var updated = await _dynamicDataRepository.UpdateWithSectionsAsync(
            formDefinition.TableName, prepared.HeaderFields, request.RecordId, prepared.Header, prepared.ToSectionRows(),
            request.ModifiedByUserId, guard, cancellationToken);
        if (!updated) throw new NotFoundException("Record", request.RecordId);
        foreach (var hook in hooks) await hook.AfterSaveAsync(hookContext, request.RecordId, CancellationToken.None);
    }
}
