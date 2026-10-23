using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Platform.Infrastructure.Persistence;

/// <summary>
/// Runs one cheap query right after startup, so the first real request after a deploy or
/// restart doesn't pay for building the EF model, Npgsql's type loading and opening a
/// connection to the (distant) database. Failure is logged, never fatal - the app still
/// starts and the first request simply pays the cost as before.
/// </summary>
public class DatabaseWarmupService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DatabaseWarmupService> _logger;

    public DatabaseWarmupService(IServiceScopeFactory scopeFactory, ILogger<DatabaseWarmupService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            var started = DateTime.UtcNow;
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Users.AsNoTracking().AnyAsync(stoppingToken);
            _logger.LogInformation("Database warm-up done in {Ms} ms.", (int)(DateTime.UtcNow - started).TotalMilliseconds);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Database warm-up failed; the first request will open the connection instead.");
        }
    }
}
