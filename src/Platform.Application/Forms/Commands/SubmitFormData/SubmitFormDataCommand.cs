using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Platform.Application.Common.Exceptions;
using Platform.Application.Common.Interfaces;

namespace Platform.Application.Forms.Commands.SubmitFormData;

/// <remarks>
/// IdempotencyKey / EnteredAtUtc come from the offline queue (redesign §9.6, owner decision 8):
/// the same key from the same person returns the original record instead of saving it again,
/// and EnteredAtUtc - when Send was pressed on the phone - is kept on a SubmissionReceipt
/// (CreatedAtUtc on the row stays the server's time). Not more than 5 minutes ahead (clock skew),
/// at most 3 days old.
/// </remarks>
public record SubmitFormDataCommand(
    Guid FormDefinitionId, IReadOnlyDictionary<string, object?> Values, Guid SubmittedByUserId,
    string? IdempotencyKey = null, DateTime? EnteredAtUtc = null) : IRequest<Guid>;

public class SubmitFormDataCommandValidator : AbstractValidator<SubmitFormDataCommand>
{
    public SubmitFormDataCommandValidator()
    {
        RuleFor(x => x.FormDefinitionId).NotEmpty();
        RuleFor(x => x.SubmittedByUserId).NotEmpty();
        RuleFor(x => x.IdempotencyKey).Matches("^[A-Za-z0-9_-]{8,64}$").When(x => x.IdempotencyKey is not null)
            .WithMessage("Idempotency-Key must be 8-64 letters, digits, '-' or '_'.");
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

    public static readonly TimeSpan EnteredAtMaxAge = TimeSpan.FromDays(3);
    public static readonly TimeSpan EnteredAtMaxSkew = TimeSpan.FromMinutes(5);

    public async Task<Guid> Handle(SubmitFormDataCommand request, CancellationToken cancellationToken)
    {
        // A repeat of a key this person already used: the entry is saved - return it (§9.6).
        if (request.IdempotencyKey is { } key && await ExistingAsync(request, key, cancellationToken) is { } earlier)
            return earlier;

        if (request.EnteredAtUtc is { } entered)
        {
            var now = DateTime.UtcNow;
            var utc = entered.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(entered, DateTimeKind.Utc) : entered.ToUniversalTime();
            if (utc > now + EnteredAtMaxSkew || utc < now - EnteredAtMaxAge)
                throw new Platform.Application.Common.Exceptions.ValidationException(
                    "submission.enteredAtInvalid", "The entry's time must be within the last 3 days and not in the future.");
        }

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

        var values = SubmissionValueValidator.Normalize(activeFields, request.Values);
        await SubmissionDataChecks.ThrowIfInvalidAsync(
            _db, _dynamicDataRepository, formDefinition.TableName, activeFields, values, null, cancellationToken);

        // Claim the key (and keep the phone's time) BEFORE writing the row: the unique index
        // lets only one of two simultaneous retries through.
        Platform.Domain.Forms.SubmissionReceipt? receipt = null;
        if (request.IdempotencyKey is not null || request.EnteredAtUtc is not null)
        {
            receipt = Platform.Domain.Forms.SubmissionReceipt.Claim(
                request.SubmittedByUserId, formDefinition.Id, request.IdempotencyKey ?? Guid.NewGuid().ToString("N"),
                request.EnteredAtUtc?.ToUniversalTime());
            _db.SubmissionReceipts.Add(receipt);
            try
            {
                await _db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException) when (request.IdempotencyKey is not null)
            {
                if (_db is DbContext context) context.Entry(receipt).State = EntityState.Detached;
                return await ExistingAsync(request, request.IdempotencyKey, cancellationToken)
                    ?? throw new Platform.Application.Common.Exceptions.ValidationException(
                        "submission.inProgress", "This entry is already being saved - try again in a moment.");
            }
        }

        Guid newRecordId;
        try
        {
            newRecordId = await _dynamicDataRepository.InsertAsync(
                formDefinition.TableName, activeFields, values, request.SubmittedByUserId, cancellationToken);
        }
        catch when (receipt is not null)
        {
            // Nothing was saved - free the key so the phone's next try can go through.
            _db.SubmissionReceipts.Remove(receipt);
            await _db.SaveChangesAsync(CancellationToken.None);
            throw;
        }
        if (receipt is not null)
        {
            receipt.Complete(newRecordId);
            await _db.SaveChangesAsync(cancellationToken);
        }

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

    /// <summary>The record already saved under this key (same person, same form), or null.
    /// A key whose row is still being written is "in progress" (400 submission.inProgress).</summary>
    private async Task<Guid?> ExistingAsync(SubmitFormDataCommand request, string key, CancellationToken cancellationToken)
    {
        var receipt = await _db.SubmissionReceipts.AsNoTracking()
            .SingleOrDefaultAsync(r => r.UserId == request.SubmittedByUserId && r.IdempotencyKey == key, cancellationToken);
        if (receipt is null) return null;
        if (receipt.FormDefinitionId != request.FormDefinitionId)
            throw new Platform.Application.Common.Exceptions.ValidationException(
                "submission.keyReused", "This Idempotency-Key was already used for another form.");
        return receipt.RecordId ?? throw new Platform.Application.Common.Exceptions.ValidationException(
            "submission.inProgress", "This entry is already being saved - try again in a moment.");
    }
}
