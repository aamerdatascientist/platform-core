using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Platform.Application.Common.Exceptions;
using Platform.Application.Common.Interfaces;
using Platform.Domain.Forms.Enums;

namespace Platform.Application.Forms.Commands.AddFieldDefinition;

public record AddFieldDefinitionCommand(
    Guid FormDefinitionId, string Code, string Label, FieldType FieldType, bool IsRequired,
    string? OptionsJson, Guid? LookupFormDefinitionId, string? ValidationRulesJson) : IRequest<Guid>;

public class AddFieldDefinitionCommandValidator : AbstractValidator<AddFieldDefinitionCommand>
{
    public AddFieldDefinitionCommandValidator()
    {
        RuleFor(x => x.FormDefinitionId).NotEmpty();
        RuleFor(x => x.Code).NotEmpty().MaximumLength(63);
        RuleFor(x => x.Label).NotEmpty().MaximumLength(200);
        RuleFor(x => x.OptionsJson).NotEmpty().When(x => x.FieldType == FieldType.Dropdown)
            .WithMessage("Dropdown fields require options.");
        RuleFor(x => x.LookupFormDefinitionId).NotEmpty().When(x => x.FieldType == FieldType.Lookup)
            .WithMessage("Lookup fields require a target form.");
    }
}

public class AddFieldDefinitionCommandHandler : IRequestHandler<AddFieldDefinitionCommand, Guid>
{
    private readonly IApplicationDbContext _db;

    public AddFieldDefinitionCommandHandler(IApplicationDbContext db) => _db = db;

    public async Task<Guid> Handle(AddFieldDefinitionCommand request, CancellationToken cancellationToken)
    {
        var formDefinition = await _db.FormDefinitions
            .Include(f => f.Versions).ThenInclude(v => v.Fields)
            .SingleOrDefaultAsync(f => f.Id == request.FormDefinitionId, cancellationToken);

        if (formDefinition is null)
            throw new NotFoundException(nameof(Platform.Domain.Forms.FormDefinition), request.FormDefinitionId);

        var draft = formDefinition.GetDraftVersion();

        var field = draft.AddField(
            request.Code, request.Label, request.FieldType, request.IsRequired,
            request.OptionsJson, request.LookupFormDefinitionId, request.ValidationRulesJson);

        _db.FieldDefinitions.Add(field);
        await _db.SaveChangesAsync(cancellationToken);

        return field.Id;
    }
}
