using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Platform.Application.Common.Interfaces;
using Platform.Domain.Identity;

namespace Platform.Application.Identity.Commands.Login;

public record LoginCommand(string Email, string Password) : IRequest<TokenPair>;

public class LoginCommandValidator : AbstractValidator<LoginCommand>
{
    public LoginCommandValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Password).NotEmpty();
    }
}

public class LoginCommandHandler : IRequestHandler<LoginCommand, TokenPair>
{
    private readonly IApplicationDbContext _db;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenService _jwtTokenService;

    public LoginCommandHandler(IApplicationDbContext db, IPasswordHasher passwordHasher, IJwtTokenService jwtTokenService)
    {
        _db = db;
        _passwordHasher = passwordHasher;
        _jwtTokenService = jwtTokenService;
    }

    public async Task<TokenPair> Handle(LoginCommand request, CancellationToken cancellationToken)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();

        var user = await _db.Users
            .Include(u => u.UserRoles)
            .SingleOrDefaultAsync(u => u.Email == normalizedEmail && !u.IsDeleted, cancellationToken);

        // Deliberately identical failure for "no such user" and "wrong password" -
        // don't let the API reveal which emails are registered.
        if (user is null || !user.IsActive || !_passwordHasher.Verify(request.Password, user.PasswordHash))
            throw new Common.Exceptions.ForbiddenAccessException("Invalid email or password.");

        var roleIds = user.UserRoles.Select(ur => ur.RoleId).ToList();
        var roleNames = await _db.Roles
            .Where(r => roleIds.Contains(r.Id))
            .Select(r => r.Name)
            .ToListAsync(cancellationToken);

        var tokenPair = _jwtTokenService.GenerateTokenPair(user.Id, user.Email, roleNames);

        // Fully qualified: Platform.Application.Identity.Commands.RefreshToken (the new
        // command's namespace) now shadows the unqualified "RefreshToken" name at this
        // enclosing-namespace scope, ahead of the `using Platform.Domain.Identity;` import.
        _db.RefreshTokens.Add(Platform.Domain.Identity.RefreshToken.Create(
            user.Id,
            _jwtTokenService.HashRefreshToken(tokenPair.RefreshToken),
            DateTime.UtcNow.AddDays(30)));
        await _db.SaveChangesAsync(cancellationToken);

        return tokenPair;
    }
}
