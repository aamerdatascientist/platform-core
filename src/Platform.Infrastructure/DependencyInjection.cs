using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Platform.Application.Analytics;
using Platform.Application.Common.Interfaces;
using Platform.Infrastructure.Files;
using Platform.Infrastructure.Identity;
using Platform.Infrastructure.Persistence;
using Platform.Infrastructure.Persistence.DynamicSchema;

namespace Platform.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseNpgsql(
                WithWarmPool(configuration.GetConnectionString("PostgresConnection")),
                npgsql => npgsql
                    .MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName)
                    // General transient-fault retry (network blips, connection resets) -
                    // not working around a Postgres-specific quirk the way the old
                    // Azure-SQL-serverless-autopause version of this call was.
                    .EnableRetryOnFailure()));

        services.AddScoped<IApplicationDbContext>(provider => provider.GetRequiredService<ApplicationDbContext>());

        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUserService, CurrentUserService>();
        services.AddSingleton<IPasswordHasher, PasswordHasher>();
        services.AddSingleton<IJwtTokenService, JwtTokenService>();

        services.AddScoped<IDynamicSchemaService, DynamicSchemaService>();
        services.AddScoped<IDynamicDataRepository, DynamicDataRepository>();
        services.AddScoped<IExecutiveOverviewRepository, ExecutiveOverviewRepository>();
        services.AddScoped<IBlobStorageService, BlobStorageService>();

        services.AddHostedService<DatabaseWarmupService>();

        return services;
    }

    // The API (Azure East Asia) and the database (Railway US West) are an ocean apart: each
    // round trip costs ~200 ms, and opening a new connection (TCP + TLS + auth) costs several.
    // Npgsql closes idle connections after 5 minutes by default, so the first sign-in after a
    // quiet spell paid seconds just reconnecting. Keep a few connections open and alive
    // unless the connection string says otherwise.
    internal static string? WithWarmPool(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString)) return connectionString;

        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        var keys = connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2)[0].Trim().Replace(" ", ""))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (!keys.Contains("MinimumPoolSize") && !keys.Contains("MinPoolSize")) builder.MinPoolSize = 3;
        if (!keys.Contains("KeepAlive")) builder.KeepAlive = 30;
        return builder.ConnectionString;
    }
}
