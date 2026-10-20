using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Platform.Application.Common.Exceptions;
using Platform.Application.Common.Interfaces;

namespace Platform.Application.Identity.Commands.ChangeOwnPassword;

/// <summary>
/// The signed-in user changes their own password. Every refresh token they hold is revoked,
/// so any other device or browser signed in as them is signed out within one access-token
/// lifetime; this device gets a fresh token pair back so it stays signed in.
///
/// A wrong current password is a 400 on CurrentPassword, deliberately not a 401: the frontend
/// answers an authenticated 401 by refreshing the session and retrying, which would be wrong
/// here.
/// </summary>
public record ChangeOwnPasswordCommand(string CurrentPassword, string NewPassword) : IRequest<TokenPair>;

public class ChangeOwnPasswordCommandValidator : AbstractValidator<ChangeOwnPasswordCommand>
{
    public ChangeOwnPasswordCommandValidator()
    {
        RuleFor(x => x.CurrentPassword).NotEmpty();
        RuleFor(x => x.NewPassword).ValidPassword();
        RuleFor(x => x.NewPassword).NotEqual(x => x.CurrentPassword)
            .WithMessage("The new password must be different from the current one.");
    }
}

public class ChangeOwnPasswordCommandHandler : IRequestHandler<ChangeOwnPasswordCommand, TokenPair>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenService _jwtTokenService;

    public ChangeOwnPasswordCommandHandler(
        IApplicationDbContext db, ICurrentUserService currentUser, IPasswordHasher passwordHasher, IJwtTokenService jwtTokenService)
    {
        _db = db;
        _currentUser = currentUser;
        _passwordHasher = passwordHasher;
        _jwtTokenService = jwtTokenService;
    }

    public async Task<TokenPair> Handle(ChangeOwnPasswordCommand request, CancellationToken cancellationToken)
    {
        var userId = _currentUser.UserId ?? throw new UnauthorizedException();

        var user = await _db.Users
            .Include(u => u.UserRoles)
            .SingleOrDefaultAsync(u => u.Id == userId && !u.IsDeleted, cancellationToken);
        if (user is null || !user.IsActive) throw new UnauthorizedException();

        if (!_passwordHasher.Verify(request.CurrentPassword, user.PasswordHash))
            throw new Common.Exceptions.ValidationException(new[]
            {
                new FluentValidation.Results.ValidationFailure(nameof(request.CurrentPassword), "The current password is wrong.")
            });

        user.ChangePasswordHash(_passwordHasher.Hash(request.NewPassword));
        await RefreshTokenRevocation.RevokeAllAsync(_db, user.Id, cancellationToken);

        var roleIds = user.UserRoles.Select(ur => ur.RoleId).ToList();
        var roleNames = await _db.Roles.Where(r => roleIds.Contains(r.Id)).Select(r => r.Name).ToListAsync(cancellationToken);
        var tokenPair = _jwtTokenService.GenerateTokenPair(user.Id, user.Email, roleNames);
        _db.RefreshTokens.Add(Platform.Domain.Identity.RefreshToken.Create(
            user.Id, _jwtTokenService.HashRefreshToken(tokenPair.RefreshToken), DateTime.UtcNow.AddDays(30)));

        await _db.SaveChangesAsync(cancellationToken);
        return tokenPair;
    }
}
