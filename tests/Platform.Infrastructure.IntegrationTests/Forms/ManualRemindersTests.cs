using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Platform.Application.Common.Interfaces;
using Platform.Application.Forms.Assignments;
using Platform.Application.Forms.Assignments.Queries.GetAssignmentDays;
using Platform.Application.Forms.Assignments.Queries.GetFormAssignments;
using Platform.Application.Notifications;
using Platform.Domain.Forms;
using Platform.Domain.Forms.Enums;
using Platform.Domain.Identity;
using Platform.Infrastructure.IntegrationTests.Analytics;
using Platform.Infrastructure.Persistence;
using Xunit;

namespace Platform.Infrastructure.IntegrationTests.Forms;

/// <summary>
/// "Remind those who haven't filled" (redesign 23, §9.5, owner decision 7): manual reminders go
/// to everyone still pending today, at most once per person/form/day per channel, apart from
/// the automatic reminder slots; and the tracking grid knows each day's entry and last reminder.
/// </summary>
public class ManualRemindersTests
{
    private sealed class FakeTelegram : ITelegramClient
    {
        public List<long> Sent { get; } = new();
        public bool Fail { get; set; }
        public Task<NotificationSendResult> SendMessageAsync(long chatId, string text, string? buttonText, string? buttonUrl, CancellationToken cancellationToken = default)
        {
            Sent.Add(chatId);
            return Task.FromResult(Fail ? new NotificationSendResult(false, null, "blocked") : new NotificationSendResult(true, "m", null));
        }
        public Task<IReadOnlyList<TelegramUpdate>> GetUpdatesAsync(long offset, int timeoutSeconds, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TelegramUpdate>>(Array.Empty<TelegramUpdate>());
    }

    private sealed class NoWhatsApp : IWhatsAppSender
    {
        public Task<NotificationSendResult> SendTemplateAsync(string to, string templateName, IReadOnlyList<string> bodyParameters, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("WhatsApp isn't configured in these tests.");
    }

    private sealed record World(ApplicationDbContext Db, FakeDynamicDataRepository Repo, FakeTelegram Telegram, AssignmentNotifier Notifier,
        FormDefinition Form, User Linked, User Unlinked, User Filled, DateOnly Today);

    private static async Task<World> CreateAsync()
    {
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var repo = new FakeDynamicDataRepository();
        var form = FormDefinition.Create("daily", "Daily report", "Daily Reports", null);
        var v = form.GetDraftVersion();
        v.AddField("crew_count", "Crew", FieldType.Number, true, null, null, null);
        v.MarkPublished();
        form.MarkPublished(v, "Data_Daily");
        db.FormDefinitions.Add(form);
        var linked = User.Create("a@x.com", "Linked", "h", null); linked.LinkTelegram(111);
        var unlinked = User.Create("b@x.com", "Unlinked", "h", null);
        var filled = User.Create("c@x.com", "Filled", "h", null); filled.LinkTelegram(333);
        db.Users.AddRange(linked, unlinked, filled);
        var today = new DateOnly(2026, 11, 7); // a Saturday (working day)
        foreach (var u in new[] { linked, unlinked, filled })
            db.FormAssignments.Add(FormAssignment.Create(form.Id, u.Id, today.AddDays(-3), null, null));
        await db.SaveChangesAsync();
        repo.Records["Data_Daily"] = new()
        {
            new RecordRow(Guid.NewGuid(), FormAssignmentTracker.SaudiDayStartUtc(today).AddHours(5), filled.Id, new Dictionary<string, object?>()),
        };
        var telegram = new FakeTelegram();
        var notifier = new AssignmentNotifier(db, repo, new NoWhatsApp(), telegram, new WhatsAppSettings(),
            new TelegramSettings { BotToken = "1:a", BotUsername = "Bot" }, new NotificationSettings { AppUrl = "https://staging.asasksa.co" },
            NullLogger<AssignmentNotifier>.Instance);
        return new World(db, repo, telegram, notifier, form, linked, unlinked, filled, today);
    }

    [Fact]
    public async Task RemindsOnlyThosePendingToday_AndCountsTheOnesWithNoChannel()
    {
        var w = await CreateAsync();
        var r = await w.Notifier.SendManualRemindersAsync(w.Today, default);
        r.Should().Be(new AssignmentNotifier.ManualReminderResult(Sent: 1, Failed: 0, AlreadyReminded: 0, NoChannel: 1));
        w.Telegram.Sent.Should().Equal(111L);
    }

    [Fact]
    public async Task ASecondPressTheSameDayDoesNotRemindAgain()
    {
        var w = await CreateAsync();
        await w.Notifier.SendManualRemindersAsync(w.Today, default);
        var again = await w.Notifier.SendManualRemindersAsync(w.Today, default);
        again.Should().Be(new AssignmentNotifier.ManualReminderResult(0, 0, 1, 1));
        w.Telegram.Sent.Should().HaveCount(1);
    }

    [Fact]
    public async Task ManualAndAutomaticRemindersDoNotUseUpEachOther()
    {
        var w = await CreateAsync();
        (await w.Notifier.SendDueRemindersAsync(w.Today, 1, default)).Should().Be(1);
        (await w.Notifier.SendManualRemindersAsync(w.Today, default)).Sent.Should().Be(1, "the automatic slot doesn't block a manual reminder");
        (await w.Notifier.SendDueRemindersAsync(w.Today, 2, default)).Should().Be(1, "and a manual one doesn't block the next automatic slot");
    }

    [Fact]
    public async Task AFailedSendIsCountedAsFailed()
    {
        var w = await CreateAsync();
        w.Telegram.Fail = true;
        (await w.Notifier.SendManualRemindersAsync(w.Today, default)).Should().Be(new AssignmentNotifier.ManualReminderResult(0, 1, 0, 1));
    }

    [Fact]
    public async Task TheGridKnowsEachDaysEntryAndLastReminder()
    {
        var w = await CreateAsync();
        await w.Notifier.SendManualRemindersAsync(w.Today, default);
        var days = await new GetAssignmentDaysQueryHandler(w.Db, w.Repo).Handle(new GetAssignmentDaysQuery(w.Today, w.Today), default);
        days.Single(d => d.UserId == w.Filled.Id).RecordId.Should().Be(w.Repo.Records["Data_Daily"][0].Id);
        days.Single(d => d.UserId == w.Linked.Id).LastRemindedAtUtc.Should().NotBeNull();
        days.Single(d => d.UserId == w.Unlinked.Id).LastRemindedAtUtc.Should().BeNull();
    }

    // Regression (2026-11-07): the list reads every assignment's whole history; reading 3 days past
    // an open-ended range overflowed DateOnly and the Form assignments page got a 500.
    [Fact]
    public async Task TheAssignmentsList_LoadsWithSubmissionsPresent()
    {
        var w = await CreateAsync();
        var list = await new GetFormAssignmentsQueryHandler(w.Db, w.Repo).Handle(new GetFormAssignmentsQuery(), default);
        list.Should().HaveCount(3);
    }

    [Fact]
    public async Task AnOpenEndedRange_DoesNotOverflow()
    {
        var w = await CreateAsync();
        var act = () => FormAssignmentTracker.LoadAsync(w.Db, w.Repo, null, null, DateOnly.MinValue, DateOnly.MaxValue, default);
        await act.Should().NotThrowAsync();
        FormAssignmentTracker.AddDaysClamped(DateOnly.MaxValue.AddDays(-1), 4).Should().Be(DateOnly.MaxValue);
        FormAssignmentTracker.AddDaysClamped(new DateOnly(2026, 11, 7), 4).Should().Be(new DateOnly(2026, 11, 11));
    }
}
