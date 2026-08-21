using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
                configuration.GetConnectionString("PostgresConnection"),
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
        services.AddScoped<IBlobStorageService, BlobStorageService>();

        return services;
    }
}
