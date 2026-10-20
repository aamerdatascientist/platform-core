using Microsoft.EntityFrameworkCore;
using Platform.Application.Common.Interfaces;

namespace Platform.Application.Identity;

public static class RefreshTokenRevocation
{
    /// <summary>
    /// Revokes every still-usable refresh token of one user (the caller saves). Their access
    /// tokens can't be recalled, but expire within 30 minutes and can't be renewed.
    /// </summary>
    public static async Task RevokeAllAsync(IApplicationDbContext db, Guid userId, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var active = await db.RefreshTokens
            .Where(rt => rt.UserId == userId && rt.RevokedAtUtc == null && rt.ExpiresAtUtc > now)
            .ToListAsync(cancellationToken);
        foreach (var token in active) token.Revoke();
    }
}
