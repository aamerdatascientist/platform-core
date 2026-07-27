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

    public SubmitFormDataCommandHandler(IApplicationDbContext db, IDynamicDataRepository dynamicDataRepository)
    {
        _db = db;
        _dynamicDataRepository = dynamicDataRepository;
    }

    public async Task<Guid> Handle(SubmitFormDataCommand request, CancellationToken cancellationToken)
    {
        var formDefinition = await _db.FormDefinitions
            .Include(f => f.Versions).ThenInclude(v => v.Fields)
            .SingleOrDefaultAsync(f => f.Id == request.FormDefinitionId, cancellationToken);

        if (formDefinition is null)
            throw new NotFoundException(nameof(Platform.Domain.Forms.FormDefinition), request.FormDefinitionId);

        var publishedVersion = formDefinition.GetPublishedVersion();
        if (publishedVersion is null || formDefinition.TableName is null)
            throw new ValidationException(new[]
            {
                new FluentValidation.Results.ValidationFailure(
                    nameof(request.FormDefinitionId), "This form has no published version to submit data against.")
            });

        var activeFields = publishedVersion.Fields.Where(f => f.IsActive).ToList();

        var missingRequired = activeFields
            .Where(f => f.IsRequired)
            .Where(f => !request.Values.ContainsKey(f.Code) || request.Values[f.Code] is null)
            .Select(f => f.Code)
            .ToList();

        if (missingRequired.Count != 0)
            throw new ValidationException(missingRequired.Select(code =>
                new FluentValidation.Results.ValidationFailure(code, $"'{code}' is required.")));

        return await _dynamicDataRepository.InsertAsync(
            formDefinition.TableName, activeFields, request.Values, request.SubmittedByUserId, cancellationToken);
    }
}
