using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Platform.Application.Common.Exceptions;
using Platform.Application.Common.Interfaces;
using Platform.Application.Forms.Assignments;
using Platform.Application.Forms.Commands.SubmitFormData;
using Platform.Application.Forms.Records;
using Platform.Domain.Forms;
using Platform.Domain.Forms.Enums;
using Platform.Infrastructure.IntegrationTests.Analytics;
using Platform.Infrastructure.Persistence;
using Xunit;

namespace Platform.Infrastructure.IntegrationTests.Forms;

/// <summary>
/// The offline queue's server side (redesign §9.6, owner decision 8): an Idempotency-Key saves an
/// entry only once; the phone's entered-at time is validated, kept apart from CreatedAtUtc, and
/// decides the assignment day and the time shown.
/// </summary>
public class OfflineQueueTests
{
    private sealed class Caller : ICurrentUserService
    {
        public Caller(Guid id) => UserId = id;
        public Guid? UserId { get; }
        public string? Email => null;
        public IReadOnlyCollection<string> Roles => new[] { "Project Manager" };
        public bool IsAuthenticated => true;
    }

    private sealed record World(ApplicationDbContext Db, FakeDynamicDataRepository Repo, FormDefinition Form, FormDefinition Other, Guid User);

    private static FormDefinition Published(string code, string table)
    {
        var form = FormDefinition.Create(code, code, "Daily Reports", null);
        var v = form.GetDraftVersion();
        v.AddField("crew_count", "Crew", FieldType.Number, true, null, null, null);
        v.MarkPublished();
        form.MarkPublished(v, table);
        return form;
    }

    private static async Task<World> CreateAsync()
    {
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var form = Published("daily", "Data_Daily");
        var other = Published("other", "Data_Other");
        db.FormDefinitions.AddRange(form, other);
        await db.SaveChangesAsync();
        return new World(db, new FakeDynamicDataRepository(), form, other, Guid.NewGuid());
    }

    private static IReadOnlyDictionary<string, object?> Values(int crew) =>
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(JsonSerializer.Serialize(new { crew_count = crew }))!.ToDictionary(kv => kv.Key, kv => (object?)kv.Value);

    private static Task<Guid> Submit(World w, Guid formId, string? key, DateTime? enteredAt = null, Guid? user = null) =>
        new SubmitFormDataCommandHandler(w.Db, w.Repo, new Caller(user ?? w.User))
            .Handle(new SubmitFormDataCommand(formId, Values(5), user ?? w.User, key, enteredAt), CancellationToken.None);

    [Fact]
    public async Task TheSameKeyTwice_SavesOnce_AndReturnsTheSameRecord()
    {
        var w = await CreateAsync();
        var first = await Submit(w, w.Form.Id, "key-0001-abcdef");
        var again = await Submit(w, w.Form.Id, "key-0001-abcdef");
        again.Should().Be(first);
        w.Repo.Records["Data_Daily"].Should().HaveCount(1);
    }

    [Fact]
    public async Task KeysArePerPerson()
    {
        var w = await CreateAsync();
        var a = await Submit(w, w.Form.Id, "shared-key-0001");
        var b = await Submit(w, w.Form.Id, "shared-key-0001", user: Guid.NewGuid());
        b.Should().NotBe(a);
        w.Repo.Records["Data_Daily"].Should().HaveCount(2);
    }

    [Fact]
    public async Task AKeyCantBeReusedForAnotherForm()
    {
        var w = await CreateAsync();
        await Submit(w, w.Form.Id, "key-0002-abcdef");
        var act = () => Submit(w, w.Other.Id, "key-0002-abcdef");
        (await act.Should().ThrowAsync<ValidationException>()).Which.Code.Should().Be("submission.keyReused");
    }

    [Theory]
    [InlineData(-4 * 24 * 60)] // older than 3 days
    [InlineData(10)]           // 10 minutes in the future
    public async Task EnteredAt_MustBeRecentAndNotInTheFuture(int minutesFromNow)
    {
        var w = await CreateAsync();
        var act = () => Submit(w, w.Form.Id, "key-0003-abcdef", DateTime.UtcNow.AddMinutes(minutesFromNow));
        (await act.Should().ThrowAsync<ValidationException>()).Which.Code.Should().Be("submission.enteredAtInvalid");
        w.Repo.Records.GetValueOrDefault("Data_Daily").Should().BeNullOrEmpty("nothing is saved");
    }

    [Fact]
    public async Task EnteredAt_IsKeptOnTheReceipt_AndShownForTheRecord()
    {
        var w = await CreateAsync();
        var entered = DateTime.UtcNow.AddDays(-2);
        var id = await Submit(w, w.Form.Id, "key-0004-abcdef", entered);
        var receipt = await w.Db.SubmissionReceipts.SingleAsync();
        (receipt.RecordId, receipt.EnteredAtUtc).Should().Be((id, entered));
        var items = await RecordViews.ToItemsAsync(w.Db, w.Repo, w.Form.Id, w.Form.GetPublishedVersion()!.Fields.ToList(), w.Repo.Records["Data_Daily"], default);
        items.Single().EnteredAtUtc.Should().Be(entered);
    }

    [Fact]
    public async Task AFailedInsert_FreesTheKeyForTheNextTry()
    {
        var w = await CreateAsync();
        w.Repo.FailNextInsert = true;
        var act = () => Submit(w, w.Form.Id, "key-0005-abcdef");
        await act.Should().ThrowAsync<InvalidOperationException>();
        (await w.Db.SubmissionReceipts.CountAsync()).Should().Be(0);
        var id = await Submit(w, w.Form.Id, "key-0005-abcdef");
        w.Repo.Records["Data_Daily"].Single().Id.Should().Be(id);
    }

    [Fact]
    public async Task AQueuedEntry_CountsForTheDayItWasEntered()
    {
        var w = await CreateAsync();
        var today = FormAssignmentTracker.SaudiToday(DateTime.UtcNow);
        var yesterday = today.AddDays(-1);
        while (!FormAssignmentTracker.IsWorkingDay(yesterday)) yesterday = yesterday.AddDays(-1);
        w.Db.FormAssignments.Add(FormAssignment.Create(w.Form.Id, w.User, yesterday.AddDays(-5), null, null));
        await w.Db.SaveChangesAsync();
        // Entered yesterday on the phone, reaching the server only today.
        await Submit(w, w.Form.Id, "key-0006-abcdef", FormAssignmentTracker.SaudiDayStartUtc(yesterday).AddHours(10));

        var (assignments, stamps) = await FormAssignmentTracker.LoadAsync(w.Db, w.Repo, null, null, yesterday, yesterday, default);
        var days = FormAssignmentTracker.BuildDays(assignments, stamps, yesterday, yesterday, today);
        days.Single().Status.Should().Be(FormAssignmentTracker.Filled);
    }

    [Fact]
    public async Task WithoutAKey_NothingChanges()
    {
        var w = await CreateAsync();
        await Submit(w, w.Form.Id, null);
        await Submit(w, w.Form.Id, null);
        w.Repo.Records["Data_Daily"].Should().HaveCount(2);
        (await w.Db.SubmissionReceipts.CountAsync()).Should().Be(0);
    }
}
