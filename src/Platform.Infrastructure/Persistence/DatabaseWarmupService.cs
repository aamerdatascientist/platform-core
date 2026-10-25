using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Platform.Application.Common.Exceptions;
using Platform.Application.Common.Interfaces;
using Platform.Application.Forms.Queries.GetFormDefinition;
using Platform.Application.Forms.Queries.GetFormSubmissions;
using Platform.Application.Forms.Queries.GetFormsList;
using Platform.Application.Identity.Commands.Login;
using Platform.Application.Identity.Commands.RefreshToken;
using Platform.Application.Identity.Queries.GetCurrentUser;

namespace Platform.Infrastructure.Persistence;

/// <summary>
/// Runs the sign-in path and the first screens' queries once right after startup, so the first
/// real visitor after a deploy or restart doesn't pay for building the EF model, compiling each
/// query, loading BCrypt/JWT code and opening a connection. Measured 2026-10-26 on production:
/// the first sign-in after a restart took 5.9 s on the server against ~0.3 s warm, and the first
/// forms list 8.3 s. People sign in rarely (tokens last 30 days), so most real sign-ins were the
/// cold one.
///
/// Nothing here signs anyone in or writes anything: sign-in and refresh run with an address and
/// a token that cannot exist, and the signed-in queries are read-only, run in-process as an
/// existing administrator. Failure is logged, never fatal - the first request then simply pays.
/// </summary>
public class DatabaseWarmupService : BackgroundService
{
    private const string WarmupEmail = "warmup@warmup.invalid";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DatabaseWarmupService> _logger;

    public DatabaseWarmupService(IServiceScopeFactory scopeFactory, ILogger<DatabaseWarmupService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var started = DateTime.UtcNow;
        try
        {
            using (var scope = _scopeFactory.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                await db.Users.AsNoTracking().AnyAsync(stoppingToken);
            }
            _logger.LogInformation("Database warm-up done in {Ms} ms.", ElapsedMs(started));

            await WarmSignInAsync(stoppingToken);
            await WarmSignedInQueriesAsync(stoppingToken);
            _logger.LogInformation("Request warm-up done in {Ms} ms.", ElapsedMs(started));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Warm-up failed after {Ms} ms; the first requests will pay the cold start instead.", ElapsedMs(started));
        }
    }

    private async Task WarmSignInAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();

        // Same handlers, validators and queries as a real sign-in/refresh; both end in the
        // ordinary "invalid credentials" answer.
        try { await sender.Send(new LoginCommand(WarmupEmail, "warm-up"), cancellationToken); }
        catch (UnauthorizedException) { }
        try { await sender.Send(new RefreshTokenCommand(Guid.NewGuid().ToString("N")), cancellationToken); }
        catch (UnauthorizedException) { }

        // The parts an unknown address skips: the password check and issuing tokens.
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        hasher.Verify("warm-up", hasher.Hash("warm-up-other"));
        var jwt = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var pair = jwt.GenerateTokenPair(Guid.NewGuid(), WarmupEmail, Array.Empty<string>());
        jwt.HashRefreshToken(pair.RefreshToken);
    }

    private async Task WarmSignedInQueriesAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var admin = await db.Users.AsNoTracking()
            .Where(u => u.IsActive && u.UserRoles.Any(ur => db.Roles.Any(r => r.Id == ur.RoleId && r.Name == "Administrator")))
            .Select(u => new { u.Id, u.Email })
            .FirstOrDefaultAsync(cancellationToken);
        if (admin is null) return;

        // ICurrentUserService reads the caller from IHttpContextAccessor, which flows with this
        // async call only - no real request ever sees this context.
        var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        accessor.HttpContext = new DefaultHttpContext
        {
            RequestServices = scope.ServiceProvider,
            User = new ClaimsPrincipal(new ClaimsIdentity(
                new[]
                {
                    new Claim(JwtRegisteredClaimNames.Sub, admin.Id.ToString()),
                    new Claim(JwtRegisteredClaimNames.Email, admin.Email),
                    new Claim(ClaimTypes.Role, "Administrator"),
                },
                authenticationType: "warm-up")),
        };
        try
        {
            var sender = scope.ServiceProvider.GetRequiredService<ISender>();
            await sender.Send(new GetCurrentUserQuery(), cancellationToken);
            var forms = await sender.Send(new GetFormsListQuery(), cancellationToken);
            foreach (var form in forms)
            {
                await sender.Send(new GetFormDefinitionQuery(form.Id), cancellationToken);
                await sender.Send(new GetFormSubmissionsQuery(form.Id), cancellationToken);
            }
        }
        finally
        {
            accessor.HttpContext = null;
        }
    }

    private static int ElapsedMs(DateTime started) => (int)(DateTime.UtcNow - started).TotalMilliseconds;
}
