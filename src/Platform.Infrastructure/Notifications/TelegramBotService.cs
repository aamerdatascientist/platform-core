using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Platform.Application.Common.Interfaces;
using Platform.Application.Notifications;

namespace Platform.Infrastructure.Notifications;

/// <summary>
/// Reads what people send the bot (long polling - no public webhook address needed) and hands
/// each message to <see cref="TelegramLinker"/>, which links "/start {code}" to a user. Off while
/// Telegram isn't configured. A 409 means another server is polling the same bot: normal for a
/// minute during a deploy (old and new container overlap), a mistake if it lasts (staging and
/// production sharing one bot) - logged as a warning either way.
/// </summary>
public class TelegramBotService : BackgroundService
{
    private const int PollSeconds = 50;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TelegramSettings _settings;
    private readonly ILogger<TelegramBotService> _logger;

    public TelegramBotService(IServiceScopeFactory scopeFactory, TelegramSettings settings, ILogger<TelegramBotService> logger)
    {
        _scopeFactory = scopeFactory;
        _settings = settings;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_settings.IsConfigured)
        {
            _logger.LogInformation("Telegram isn't configured - the bot isn't listening.");
            return;
        }

        // Let the app finish starting (and the warm-up run) before the first long poll.
        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
        _logger.LogInformation("Telegram bot @{Bot} listening.", _settings.BotUsername!.TrimStart('@'));

        long offset = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var client = scope.ServiceProvider.GetRequiredService<ITelegramClient>();
                var updates = await client.GetUpdatesAsync(offset, PollSeconds, stoppingToken);
                foreach (var update in updates)
                {
                    offset = update.UpdateId + 1;
                    await HandleAsync(update, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (TelegramApiException ex) when (ex.StatusCode == 409)
            {
                _logger.LogWarning("Another server is reading this Telegram bot (409); retrying in 30 s. If this lasts, staging and production share a bot.");
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Reading Telegram updates failed; retrying in 15 s.");
                await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
            }
        }
    }

    private async Task HandleAsync(TelegramUpdate update, CancellationToken stoppingToken)
    {
        try
        {
            // A fresh scope per message: one bad message mustn't leave a broken DbContext behind.
            using var scope = _scopeFactory.CreateScope();
            var linker = scope.ServiceProvider.GetRequiredService<TelegramLinker>();
            var reply = await linker.HandleAsync(update, DateTime.UtcNow, stoppingToken);
            if (reply is not null && update.ChatId is { } chatId)
            {
                var client = scope.ServiceProvider.GetRequiredService<ITelegramClient>();
                var result = await client.SendMessageAsync(chatId, reply, null, null, stoppingToken);
                if (!result.Success) _logger.LogWarning("Telegram reply failed: {Error}", result.Error);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Handling Telegram update {UpdateId} failed; skipped.", update.UpdateId);
        }
    }
}
