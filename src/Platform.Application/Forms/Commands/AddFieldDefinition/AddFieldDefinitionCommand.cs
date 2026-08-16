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

        var draft = formDefinition.GetDraftVersionOrThrow();

        Platform.Domain.Forms.FieldDefinition field;
        try
        {
            field = draft.AddField(
                request.Code, request.Label, request.FieldType, request.IsRequired,
                request.OptionsJson, request.LookupFormDefinitionId, request.ValidationRulesJson);
        }
        catch (ArgumentException ex) when (ex.ParamName is null)
        {
            // FieldDefinition.Create's NormalizeColumnName throws this specific shape of
            // ArgumentException (no ParamName) when Code doesn't normalize to a valid SQL
            // identifier - most commonly non-Latin input, since Code becomes a physical
            // column name and can't be. The other ArgumentExceptions AddField/Create can
            // throw (missing OptionsJson/LookupFormDefinitionId) always set ParamName, so
            // this guard is what keeps this catch scoped to the Code case specifically.
            // Code is given a stable identity ("form.field.codeMustBeLatin") rather than
            // just a message so the frontend can show this fully localized, not just in
            // whatever language the backend happens to write English strings in.
            throw new Common.Exceptions.ValidationException(
                "form.field.codeMustBeLatin",
                "Code must be Latin letters, digits, and underscores only, starting with a letter " +
                "(e.g. 'quantity_received') - it becomes a database column name. Use 'Field name' for " +
                "the human-readable label shown on the form, which can be in any language.");
        }

        _db.FieldDefinitions.Add(field);
        await _db.SaveChangesAsync(cancellationToken);

        return field.Id;
    }
}
