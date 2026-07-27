using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Platform.Application.Common.Interfaces;
using Platform.Domain.Identity;

namespace Platform.Application.Identity.Commands.RegisterUser;

public record RegisterUserCommand(
    string Email, string DisplayName, string Password, Guid? DepartmentId, Guid DefaultRoleId) : IRequest<Guid>;

public class RegisterUserCommandValidator : AbstractValidator<RegisterUserCommand>
{
    public RegisterUserCommandValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Password).NotEmpty().MinimumLength(10)
            .WithMessage("Password must be at least 10 characters.");
        RuleFor(x => x.DefaultRoleId).NotEmpty();
    }
}

public class RegisterUserCommandHandler : IRequestHandler<RegisterUserCommand, Guid>
{
    private readonly IApplicationDbContext _db;
    private readonly IPasswordHasher _passwordHasher;

    public RegisterUserCommandHandler(IApplicationDbContext db, IPasswordHasher passwordHasher)
    {
        _db = db;
        _passwordHasher = passwordHasher;
    }

    public async Task<Guid> Handle(RegisterUserCommand request, CancellationToken cancellationToken)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();

        var emailTaken = await _db.Users.AnyAsync(u => u.Email == normalizedEmail && !u.IsDeleted, cancellationToken);
        if (emailTaken)
            throw new Common.Exceptions.ValidationException(new[]
            {
                new FluentValidation.Results.ValidationFailure(nameof(request.Email), "Email is already registered.")
            });

        var user = User.Create(
            normalizedEmail, request.DisplayName, _passwordHasher.Hash(request.Password), request.DepartmentId);
        user.AssignRole(request.DefaultRoleId);

        _db.Users.Add(user);
        await _db.SaveChangesAsync(cancellationToken);

        return user.Id;
    }
}
