using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Platform.Application.Common.Exceptions;
using Platform.Application.Common.Interfaces;

namespace Platform.Application.Identity.Commands.RefreshToken;

public record RefreshTokenCommand(string RefreshToken) : IRequest<TokenPair>;

public class RefreshTokenCommandValidator : AbstractValidator<RefreshTokenCommand>
{
    public RefreshTokenCommandValidator() => RuleFor(x => x.RefreshToken).NotEmpty();
}

public class RefreshTokenCommandHandler : IRequestHandler<RefreshTokenCommand, TokenPair>
{
    private readonly IApplicationDbContext _db;
    private readonly IJwtTokenService _jwtTokenService;

    public RefreshTokenCommandHandler(IApplicationDbContext db, IJwtTokenService jwtTokenService)
    {
        _db = db;
        _jwtTokenService = jwtTokenService;
    }

    public async Task<TokenPair> Handle(RefreshTokenCommand request, CancellationToken cancellationToken)
    {
        var tokenHash = _jwtTokenService.HashRefreshToken(request.RefreshToken);

        var existing = await _db.RefreshTokens.SingleOrDefaultAsync(rt => rt.TokenHash == tokenHash, cancellationToken);

        // Deliberately identical failure whether the token is unknown, expired, or already
        // revoked - same principle as LoginCommandHandler not revealing which emails exist.
        if (existing is null || !existing.IsActive)
            throw new ForbiddenAccessException("Invalid or expired refresh token.");

        var user = await _db.Users
            .Include(u => u.UserRoles)
            .SingleOrDefaultAsync(u => u.Id == existing.UserId && !u.IsDeleted, cancellationToken);

        if (user is null || !user.IsActive)
            throw new ForbiddenAccessException("Invalid or expired refresh token.");

        var roleIds = user.UserRoles.Select(ur => ur.RoleId).ToList();
        var roleNames = await _db.Roles.Where(r => roleIds.Contains(r.Id)).Select(r => r.Name).ToListAsync(cancellationToken);

        // Rotation: the old refresh token is revoked the instant it's redeemed, and a brand
        // new one takes its place. A refresh token is single-use by design - if it's ever
        // replayed (stolen, then used after the legitimate client already refreshed), the
        // second attempt finds it already revoked and fails. That failure IS the security
        // property, not a bug to work around.
        existing.Revoke();

        var newTokenPair = _jwtTokenService.GenerateTokenPair(user.Id, user.Email, roleNames);

        // Direct Add() on the DbSet, not reached through a tracked parent's navigation
        // collection - the EF tracking gotcha documented in CLAUDE.md doesn't apply here.
        _db.RefreshTokens.Add(Platform.Domain.Identity.RefreshToken.Create(
            user.Id, _jwtTokenService.HashRefreshToken(newTokenPair.RefreshToken), DateTime.UtcNow.AddDays(30)));

        await _db.SaveChangesAsync(cancellationToken);

        return newTokenPair;
    }
}
