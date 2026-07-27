using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Platform.Application.Common.Interfaces;
using Platform.Domain.Forms;

namespace Platform.Application.Forms.Commands.CreateFormDefinition;

public record CreateFormDefinitionCommand(
    string Code, string Name, string ModuleName, string? Description) : IRequest<Guid>;

public class CreateFormDefinitionCommandValidator : AbstractValidator<CreateFormDefinitionCommand>
{
    public CreateFormDefinitionCommandValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(64);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.ModuleName).NotEmpty().MaximumLength(100);
    }
}

public class CreateFormDefinitionCommandHandler : IRequestHandler<CreateFormDefinitionCommand, Guid>
{
    private readonly IApplicationDbContext _db;

    public CreateFormDefinitionCommandHandler(IApplicationDbContext db) => _db = db;

    public async Task<Guid> Handle(CreateFormDefinitionCommand request, CancellationToken cancellationToken)
    {
        var codeTaken = await _db.FormDefinitions.AnyAsync(f => f.Code == request.Code, cancellationToken);
        if (codeTaken)
            throw new Common.Exceptions.ValidationException(new[]
            {
                new FluentValidation.Results.ValidationFailure(
                    nameof(request.Code), $"A form with code '{request.Code}' already exists.")
            });

        var formDefinition = FormDefinition.Create(request.Code, request.Name, request.ModuleName, request.Description);

        _db.FormDefinitions.Add(formDefinition);
        // The initial draft FormVersion is created inside FormDefinition.Create() but EF Core
        // only tracks it once the aggregate root is added - no separate Add() call needed
        // as long as the navigation property is configured for cascade, see FormConfigurations.
        await _db.SaveChangesAsync(cancellationToken);

        return formDefinition.Id;
    }
}
