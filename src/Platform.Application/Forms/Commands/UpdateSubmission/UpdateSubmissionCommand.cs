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

    public UpdateSubmissionCommandHandler(IApplicationDbContext db, IDynamicDataRepository dynamicDataRepository)
    {
        _db = db;
        _dynamicDataRepository = dynamicDataRepository;
    }

    public async Task Handle(UpdateSubmissionCommand request, CancellationToken cancellationToken)
    {
        var formDefinition = await _db.FormDefinitions
            .Include(f => f.Versions).ThenInclude(v => v.Fields)
            .SingleOrDefaultAsync(f => f.Id == request.FormDefinitionId, cancellationToken);

        if (formDefinition is null)
            throw new NotFoundException(nameof(Domain.Forms.FormDefinition), request.FormDefinitionId);

        var publishedVersion = formDefinition.GetPublishedVersion();
        if (publishedVersion is null || formDefinition.TableName is null)
            throw new NotFoundException("Record", request.RecordId);

        var activeFields = publishedVersion.Fields.Where(f => f.IsActive).ToList();

        var validationErrors = SubmissionValueValidator.Validate(activeFields, request.Values);
        if (validationErrors.Count != 0)
            throw new Common.Exceptions.ValidationException(validationErrors.SelectMany(kv =>
                kv.Value.Select(msg => new FluentValidation.Results.ValidationFailure(kv.Key, msg))));

        var values = SubmissionValueValidator.Normalize(activeFields, request.Values);
        await SubmissionDataChecks.ThrowIfInvalidAsync(
            _db, _dynamicDataRepository, formDefinition.TableName, activeFields, values, request.RecordId, cancellationToken);

        var updated = await _dynamicDataRepository.UpdateAsync(
            formDefinition.TableName, activeFields, request.RecordId, values, request.ModifiedByUserId, cancellationToken);

        if (!updated) throw new NotFoundException("Record", request.RecordId);
    }
}
