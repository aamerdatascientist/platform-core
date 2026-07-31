using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Platform.Application.Common.Interfaces;

namespace Platform.Application.Identity.Commands.CreateRole;

public record CreateRoleCommand(string Name, string? Description) : IRequest<Guid>;

public class CreateRoleCommandValidator : AbstractValidator<CreateRoleCommand>
{
    public CreateRoleCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
    }
}

public class CreateRoleCommandHandler : IRequestHandler<CreateRoleCommand, Guid>
{
    private readonly IApplicationDbContext _db;

    public CreateRoleCommandHandler(IApplicationDbContext db) => _db = db;

    public async Task<Guid> Handle(CreateRoleCommand request, CancellationToken cancellationToken)
    {
        var nameTaken = await _db.Roles.AnyAsync(r => r.Name == request.Name, cancellationToken);
        if (nameTaken)
            throw new Common.Exceptions.ValidationException(new[]
            {
                new FluentValidation.Results.ValidationFailure(nameof(request.Name), $"A role named '{request.Name}' already exists.")
            });

        var role = Platform.Domain.Identity.Role.Create(request.Name, request.Description);
        _db.Roles.Add(role);
        await _db.SaveChangesAsync(cancellationToken);

        return role.Id;
    }
}
