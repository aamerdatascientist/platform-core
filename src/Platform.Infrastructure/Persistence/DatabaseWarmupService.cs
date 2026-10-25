using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Json;
using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Platform.Application.Common.Exceptions;
using Platform.Application.Common.Interfaces;
using Platform.Application.Files.Commands.UploadFile;
using Platform.Application.Forms.Commands.SubmitFormData;
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
    private readonly IHostApplicationLifetime _lifetime;
    private readonly IServer _server;
    private readonly ILogger<DatabaseWarmupService> _logger;

    public DatabaseWarmupService(
        IServiceScopeFactory scopeFactory, IHostApplicationLifetime lifetime, IServer server,
        ILogger<DatabaseWarmupService> logger)
    {
        _scopeFactory = scopeFactory;
        _lifetime = lifetime;
        _server = server;
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
            await WarmHttpSignInAsync(stoppingToken);
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
        // ordinary rejection (401 for sign-in, 403 for an unknown refresh token).
        try { await sender.Send(new LoginCommand(WarmupEmail, "warm-up"), cancellationToken); }
        catch (UnauthorizedException) { }
        try { await sender.Send(new RefreshTokenCommand(Guid.NewGuid().ToString("N")), cancellationToken); }
        catch (ForbiddenAccessException) { }

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

            // Submitting and uploading, stopped before anything is saved: a form that can't
            // exist, and a field code that can't exist (checked before the file is touched).
            try { await sender.Send(new SubmitFormDataCommand(Guid.NewGuid(), new Dictionary<string, object?>(), admin.Id), cancellationToken); }
            catch (NotFoundException) { }
            if (forms.Count > 0)
            {
                try
                {
                    await sender.Send(new UploadFileCommand(
                        forms[0].Id, Guid.NewGuid(), "__warm-up__", Stream.Null, "warm-up.jpg", "image/jpeg", 0, admin.Id),
                        cancellationToken);
                }
                catch (ValidationException) { }
            }
            scope.ServiceProvider.GetRequiredService<IBlobStorageService>();
        }
        finally
        {
            accessor.HttpContext = null;
        }
    }

    // One sign-in attempt through the app's own HTTP pipeline (JSON binding, rate limiter,
    // exception-to-401 mapping), which the in-process calls above never touch. Measured on
    // staging: without it the first sign-in attempt after a restart still took ~1.9 s.
    private async Task WarmHttpSignInAsync(CancellationToken cancellationToken)
    {
        var listening = new TaskCompletionSource();
        using (_lifetime.ApplicationStarted.Register(() => listening.TrySetResult()))
            await listening.Task.WaitAsync(cancellationToken);

        var address = _server.Features.Get<IServerAddressesFeature>()?.Addresses
            .FirstOrDefault(a => a.StartsWith("http://", StringComparison.OrdinalIgnoreCase));
        if (address is null) return;
        var uri = new UriBuilder(address.Replace("://+", "://localhost").Replace("://*", "://localhost"));
        if (uri.Host is "0.0.0.0" or "[::]") uri.Host = "localhost";

        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        {
            BaseAddress = uri.Uri,
            Timeout = TimeSpan.FromSeconds(30),
        };
        using var response = await http.PostAsJsonAsync(
            "/api/auth/login", new { email = WarmupEmail, password = "warm-up" }, cancellationToken);
    }

    private static int ElapsedMs(DateTime started) => (int)(DateTime.UtcNow - started).TotalMilliseconds;
}
