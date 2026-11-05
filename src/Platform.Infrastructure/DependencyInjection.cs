using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Platform.Application.Analytics;
using Platform.Application.Common.Interfaces;
using Platform.Infrastructure.Files;
using Platform.Infrastructure.Identity;
using Platform.Infrastructure.Notifications;
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
        // One Azure client for the app (it's thread-safe), so uploads reuse its connection to
        // storage instead of each opening a new one.
        services.AddSingleton<IBlobStorageService, BlobStorageService>();

        services.AddHostedService<DatabaseWarmupService>();

        // Assignment notifications. Each channel is off until its settings are there; with
        // none configured the reminder scheduler and the bot do nothing.
        // - Notifications: AppUrl (links in messages), ReminderTimes (Saudi, default 09:00/14:00).
        // - Telegram: BotToken + BotUsername (one bot per environment - see TelegramSettings).
        // - WhatsApp (Meta Cloud API): PhoneNumberId + AccessToken.
        services.AddSingleton(configuration.GetSection("Notifications").Get<NotificationSettings>() ?? new NotificationSettings());
        services.AddSingleton(configuration.GetSection("Telegram").Get<TelegramSettings>() ?? new TelegramSettings());
        services.AddSingleton(configuration.GetSection("WhatsApp").Get<WhatsAppSettings>() ?? new WhatsAppSettings());
        services.AddHttpClient<IWhatsAppSender, WhatsAppSender>(client => client.Timeout = TimeSpan.FromSeconds(15));
        // Longer than a long poll (50 s); each message has its own 15 s limit (TelegramClient).
        services.AddHttpClient<ITelegramClient, TelegramClient>(client => client.Timeout = TimeSpan.FromSeconds(70));
        services.AddHostedService<AssignmentReminderService>();
        services.AddHostedService<TelegramBotService>();

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
