using Platform.Application.Common.Interfaces;
using Platform.Infrastructure.Files;
using Platform.Infrastructure.Persistence;

namespace Platform.Api.Health;

internal static class StagingHealthEndpoints
{
    public static void Map(WebApplication app)
    {
        if (!string.Equals(app.Configuration["WEBSITE_SITE_NAME"], "asas-api-staging", StringComparison.OrdinalIgnoreCase))
            return;

        var health = app.MapGroup("/health");
        health.MapGet("/live", (Delegate)((HttpContext context) => Status(context, true)));
        health.MapGet("/database", (Delegate)((HttpContext context) => Probe(context, (services, cancellation) =>
            services.GetRequiredService<ApplicationDbContext>().Database.CanConnectAsync(cancellation))))
            .RequireRateLimiting(RateLimitPolicies.HealthDependencies);
        health.MapGet("/attachments", (Delegate)((HttpContext context) => Probe(context, (services, cancellation) =>
        {
            if (services.GetRequiredService<IConfiguration>()["Files:ContainerName"] != "attachments-staging")
                return Task.FromResult(false);

            return services.GetRequiredService<IBlobStorageService>() is BlobStorageService storage
                ? storage.ContainerExistsAsync(cancellation)
                : Task.FromResult(false);
        }))).RequireRateLimiting(RateLimitPolicies.HealthDependencies);
    }

    private static async Task<IResult> Probe(
        HttpContext context, Func<IServiceProvider, CancellationToken, Task<bool>> check)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            return Status(context, await check(context.RequestServices, timeout.Token));
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !context.RequestAborted.IsCancellationRequested)
        {
            return Status(context, false);
        }
    }

    private static IResult Status(HttpContext context, bool healthy)
    {
        context.Response.Headers.CacheControl = "no-store";
        return Results.StatusCode(healthy ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable);
    }
}
