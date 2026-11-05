using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Platform.Application.Common.Interfaces;
using Platform.Application.Notifications;
using Platform.Domain.Forms;
using Platform.Domain.Identity;
using Platform.Domain.Notifications;
using Platform.Infrastructure.IntegrationTests.Analytics;
using Platform.Infrastructure.Persistence;
using Xunit;

namespace Platform.Infrastructure.IntegrationTests.Forms;

/// <summary>
/// Telegram: linking a chat through the bot's /start code, and assignment messages going out on
/// the channels a user has - against an in-memory database and a fake Telegram, no network.
/// </summary>
public class TelegramNotificationTests
{
    private const long ChatId = 7_123_456_789;
    private static readonly DateTime Now = new(2026, 11, 5, 9, 0, 0, DateTimeKind.Utc);

    private sealed class FakeTelegram : ITelegramClient
    {
        public List<(long ChatId, string Text, string? ButtonText, string? ButtonUrl)> Sent { get; } = new();

        public Task<NotificationSendResult> SendMessageAsync(
            long chatId, string text, string? buttonText, string? buttonUrl, CancellationToken cancellationToken = default)
        {
            Sent.Add((chatId, text, buttonText, buttonUrl));
            return Task.FromResult(new NotificationSendResult(true, Sent.Count.ToString(), null));
        }

        public Task<IReadOnlyList<TelegramUpdate>> GetUpdatesAsync(long offset, int timeoutSeconds, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TelegramUpdate>>(Array.Empty<TelegramUpdate>());
    }

    private sealed class FakeWhatsApp : IWhatsAppSender
    {
        public int Calls { get; private set; }

        public Task<NotificationSendResult> SendTemplateAsync(
            string to, string templateName, IReadOnlyList<string> bodyParameters, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(new NotificationSendResult(true, "wamid", null));
        }
    }

    private static ApplicationDbContext NewDb() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static User NewUser(ApplicationDbContext db, string name = "Qasim")
    {
        var user = User.Create($"{name.ToLowerInvariant()}@example.com", name, "hash", null);
        db.Users.Add(user);
        return user;
    }

    private static TelegramSettings Telegram(bool configured = true) =>
        configured ? new TelegramSettings { BotToken = "123:abc", BotUsername = "AsasStagingBot" } : new TelegramSettings();

    private static AssignmentNotifier Notifier(
        ApplicationDbContext db, FakeTelegram telegram, FakeWhatsApp? whatsApp = null, string appUrl = "https://staging.asasksa.co",
        WhatsAppSettings? whatsAppSettings = null) =>
        new(db, new FakeDynamicDataRepository(), whatsApp ?? new FakeWhatsApp(), telegram,
            whatsAppSettings ?? new WhatsAppSettings(), Telegram(), new NotificationSettings { AppUrl = appUrl },
            NullLogger<AssignmentNotifier>.Instance);

    // ---- /start parsing and link codes ----

    [Theory]
    [InlineData("/start abc123", "start", "abc123")]
    [InlineData("  /start   abc123  ", "start", "abc123")]
    [InlineData("/start@AsasStagingBot abc123", "start", "abc123")]
    [InlineData("/START", "start", null)]
    [InlineData("/stop", "stop", null)]
    [InlineData("hello", null, null)]
    [InlineData("", null, null)]
    [InlineData(null, null, null)]
    public void Commands_AreParsed(string? text, string? command, string? argument) =>
        TelegramLinker.ParseCommand(text).Should().Be((command, argument));

    [Fact]
    public void LinkCodes_FitTelegramsStartParameter()
    {
        var code = TelegramLinker.NewCode();
        code.Should().HaveLength(32).And.MatchRegex("^[A-Za-z0-9_-]+$");
        TelegramLinker.NewCode().Should().NotBe(code);
        Telegram().LinkFor(code).Should().Be($"https://t.me/AsasStagingBot?start={code}");
    }

    // ---- linking through the bot ----

    [Fact]
    public async Task Start_WithAnOpenCode_LinksTheChat_AndUsesUpTheCode()
    {
        await using var db = NewDb();
        var user = NewUser(db);
        user.StartTelegramLink("code-1", Now.AddDays(7));
        await db.SaveChangesAsync();

        var reply = await new TelegramLinker(db).HandleAsync(new TelegramUpdate(1, ChatId, "/start code-1"), Now, default);

        reply.Should().Contain("تم ربط حسابك").And.Contain("Qasim");
        user.TelegramChatId.Should().Be(ChatId);
        user.TelegramLinkCode.Should().BeNull();

        // Telegram re-sends an unconfirmed update after a restart: no "invalid link" scare.
        var again = await new TelegramLinker(db).HandleAsync(new TelegramUpdate(1, ChatId, "/start code-1"), Now, default);
        again.Should().Contain("مربوط بالفعل");
    }

    [Fact]
    public async Task Start_WithAnExpiredOrUnknownCode_LinksNothing()
    {
        await using var db = NewDb();
        var user = NewUser(db);
        user.StartTelegramLink("old-code", Now.AddMinutes(-1));
        await db.SaveChangesAsync();
        var linker = new TelegramLinker(db);

        (await linker.HandleAsync(new TelegramUpdate(1, ChatId, "/start old-code"), Now, default)).Should().Contain("غير صالح");
        (await linker.HandleAsync(new TelegramUpdate(2, ChatId, "/start nope"), Now, default)).Should().Contain("غير صالح");
        user.TelegramChatId.Should().BeNull();
    }

    [Fact]
    public async Task Start_ForADeactivatedUser_IsRefused()
    {
        await using var db = NewDb();
        var user = NewUser(db);
        user.StartTelegramLink("code-1", Now.AddDays(7));
        user.Deactivate();
        await db.SaveChangesAsync();

        (await new TelegramLinker(db).HandleAsync(new TelegramUpdate(1, ChatId, "/start code-1"), Now, default))
            .Should().Contain("غير مفعّل");
        user.TelegramChatId.Should().BeNull();
    }

    [Fact]
    public async Task Stop_UnlinksEveryUserOnThatChat()
    {
        await using var db = NewDb();
        var a = NewUser(db, "A");
        var b = NewUser(db, "B");
        a.LinkTelegram(ChatId);
        b.LinkTelegram(ChatId);
        await db.SaveChangesAsync();

        (await new TelegramLinker(db).HandleAsync(new TelegramUpdate(1, ChatId, "/stop"), Now, default)).Should().Contain("إلغاء الربط");
        a.TelegramChatId.Should().BeNull();
        b.TelegramChatId.Should().BeNull();
    }

    [Fact]
    public async Task GroupChatsAndNonTextUpdates_AreIgnored()
    {
        await using var db = NewDb();
        (await new TelegramLinker(db).HandleAsync(new TelegramUpdate(1, null, "/start code"), Now, default)).Should().BeNull();
    }

    // ---- messages ----

    [Fact]
    public async Task Assigning_SendsTelegram_ToLinkedUsersOnly_WithAButtonToMyAssignments()
    {
        await using var db = NewDb();
        var linked = NewUser(db, "Qasim");
        linked.LinkTelegram(ChatId);
        var notLinked = NewUser(db, "Hazem");
        var form = FormDefinition.Create("daily-progress-report", "Daily progress report", "Daily Reports", null);
        db.FormDefinitions.Add(form);
        var start = new DateOnly(2026, 11, 7);
        var a1 = FormAssignment.Create(form.Id, linked.Id, start, null, null);
        var a2 = FormAssignment.Create(form.Id, notLinked.Id, start, null, null);
        db.FormAssignments.AddRange(a1, a2);
        await db.SaveChangesAsync();
        var telegram = new FakeTelegram();
        var whatsApp = new FakeWhatsApp();

        await Notifier(db, telegram, whatsApp).NotifyAssignedAsync(new[] { a1.Id, a2.Id }, default);

        telegram.Sent.Should().ContainSingle();
        var sent = telegram.Sent[0];
        sent.ChatId.Should().Be(ChatId);
        sent.Text.Should().Contain("Qasim").And.Contain("Daily progress report").And.Contain("7 نوفمبر 2026");
        sent.ButtonUrl.Should().Be("https://staging.asasksa.co/assignments");
        whatsApp.Calls.Should().Be(0, "WhatsApp isn't configured");

        var log = await db.NotificationLogs.SingleAsync();
        log.Channel.Should().Be(AssignmentNotifier.TelegramChannel);
        log.Status.Should().Be(NotificationLog.Sent);
        log.Recipient.Should().Be(ChatId.ToString());
    }

    [Fact]
    public async Task WithBothChannelsLinked_EachGetsTheMessage()
    {
        await using var db = NewDb();
        var user = NewUser(db);
        user.LinkTelegram(ChatId);
        user.SetPhoneNumber("966551234567");
        var form = FormDefinition.Create("f", "Form", "Ops", null);
        db.FormDefinitions.Add(form);
        var assignment = FormAssignment.Create(form.Id, user.Id, new DateOnly(2026, 11, 7), null, null);
        db.FormAssignments.Add(assignment);
        await db.SaveChangesAsync();
        var telegram = new FakeTelegram();
        var whatsApp = new FakeWhatsApp();

        await Notifier(db, telegram, whatsApp, whatsAppSettings: new WhatsAppSettings { PhoneNumberId = "1", AccessToken = "t" })
            .NotifyAssignedAsync(new[] { assignment.Id }, default);

        telegram.Sent.Should().ContainSingle();
        whatsApp.Calls.Should().Be(1);
        (await db.NotificationLogs.Select(n => n.Channel).ToListAsync())
            .Should().BeEquivalentTo(new[] { AssignmentNotifier.TelegramChannel, AssignmentNotifier.WhatsAppChannel });
    }

    [Fact]
    public async Task WithoutAnAppUrl_TheMessageGoesWithoutAButton()
    {
        await using var db = NewDb();
        var user = NewUser(db);
        user.LinkTelegram(ChatId);
        await db.SaveChangesAsync();
        var telegram = new FakeTelegram();

        var result = await Notifier(db, telegram, appUrl: "").SendTestAsync(user.Id, AssignmentNotifier.TelegramChannel, default);

        result.Success.Should().BeTrue();
        telegram.Sent.Should().ContainSingle().Which.ButtonUrl.Should().BeNull();
    }

    [Fact]
    public async Task TestMessage_ToAnUnlinkedUser_SaysSo()
    {
        await using var db = NewDb();
        var user = NewUser(db);
        await db.SaveChangesAsync();
        var telegram = new FakeTelegram();

        var result = await Notifier(db, telegram).SendTestAsync(user.Id, AssignmentNotifier.TelegramChannel, default);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("hasn't connected Telegram");
        telegram.Sent.Should().BeEmpty();
    }

    // ---- reminder times ----

    [Fact]
    public void ReminderTimes_DefaultTo0900And1400_AndASettingReplacesThem()
    {
        new NotificationSettings().EffectiveReminderTimes.Should().Equal("09:00", "14:00");
        new NotificationSettings { ReminderTimes = new[] { "08:30" } }.EffectiveReminderTimes.Should().Equal("08:30");
    }

    [Fact]
    public void ReminderTimes_FromConfiguration_ReplaceTheDefaults()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Notifications:ReminderTimes:0"] = "10:00" })
            .Build();
        var settings = config.GetSection("Notifications").Get<NotificationSettings>()!;
        settings.EffectiveReminderTimes.Should().Equal("10:00");
    }
}
