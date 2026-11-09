using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Platform.Application.Common.Behaviors;
using Platform.Application.Common.Exceptions;
using Platform.Application.Common.Interfaces;
using Platform.Application.Forms.Records;
using Platform.Domain.Files;
using Platform.Domain.Forms;
using Platform.Domain.Forms.Enums;
using Platform.Domain.Identity;
using Platform.Infrastructure.IntegrationTests.Analytics;
using Platform.Infrastructure.Persistence;
using Xunit;

namespace Platform.Infrastructure.IntegrationTests.Forms;

/// <summary>
/// Redesign phase 7 (HANDOFF §9.3/§9.4): My entries returns only the caller's own records on
/// forms they can access; the record detail is for its sender or an administrator AND needs
/// access to the form (owner decision 2026-11-06) - a user without form access gets 403 on both.
/// Requests go through the real FormAccessBehavior, as in the API.
/// </summary>
public class MyEntriesAndRecordsTests
{
    private sealed class Caller : ICurrentUserService
    {
        public Caller(Guid id, params string[] roles) { UserId = id; Roles = roles; }
        public Guid? UserId { get; }
        public string? Email => null;
        public IReadOnlyCollection<string> Roles { get; }
        public bool IsAuthenticated => true;
    }

    private sealed class World
    {
        public ApplicationDbContext Db = default!;
        public FakeDynamicDataRepository Repo = new();
        public FormDefinition Restricted = default!;   // PM role only
        public FormDefinition Open = default!;         // everyone
        public Guid Pm = Guid.NewGuid(), Pm2 = Guid.NewGuid(), Outsider = Guid.NewGuid(), Admin = Guid.NewGuid();
        public Caller PmCaller => new(Pm, "Project Manager");
        public Caller Pm2Caller => new(Pm2, "Project Manager");
        public Caller OutsiderCaller => new(Outsider, "Viewer");
        public Caller AdminCaller => new(Admin, "Administrator");
        public RecordRow PmRow = default!, OutsiderRowOnRestricted = default!, OutsiderRowOnOpen = default!, Pm2Row = default!;
    }

    private static FormDefinition Published(string code, string table)
    {
        var form = FormDefinition.Create(code, code, "Daily Reports", null);
        var v = form.GetDraftVersion();
        v.AddField("crew_count", "Crew", FieldType.Number, true, null, null, null);
        v.AddField("notes", "Notes", FieldType.ShortText, false, null, null, null);
        v.MarkPublished();
        form.MarkPublished(v, table);
        return form;
    }

    private static RecordRow Row(Guid by, DateTime at, int crew, string notes = "") =>
        new(Guid.NewGuid(), at, by, new Dictionary<string, object?> { ["crew_count"] = crew, ["notes"] = notes });

    private static async Task<World> CreateAsync()
    {
        var w = new World { Db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options) };
        var pmRole = Role.Create("Project Manager", null);
        w.Db.Roles.AddRange(pmRole, Role.Create("Viewer", null), Role.Create("Administrator", null, true));
        w.Restricted = Published("restricted", "Data_Restricted");
        w.Open = Published("open", "Data_Open");
        w.Db.FormDefinitions.AddRange(w.Restricted, w.Open);
        w.Db.FormDefinitionRoles.AddRange(w.Restricted.SetAllowedRoles(new[] { pmRole.Id }));
        var t0 = new DateTime(2026, 11, 7, 6, 0, 0, DateTimeKind.Utc);
        w.PmRow = Row(w.Pm, t0, 10);
        w.Pm2Row = Row(w.Pm2, t0.AddMinutes(1), 11);
        // Sent while the outsider still had access; they've lost it since.
        w.OutsiderRowOnRestricted = Row(w.Outsider, t0.AddMinutes(2), 12);
        w.OutsiderRowOnOpen = Row(w.Outsider, t0.AddMinutes(3), 13);
        w.Repo.Records["Data_Restricted"] = new() { w.PmRow, w.Pm2Row, w.OutsiderRowOnRestricted };
        w.Repo.Records["Data_Open"] = new() { w.OutsiderRowOnOpen };
        w.Db.FileMetadataEntries.Add(FileMetadata.Create(w.Restricted.Id, w.PmRow.Id, "photo", "blob", "a.jpg", "image/jpeg", 10));
        await w.Db.SaveChangesAsync();
        return w;
    }

    /// <summary>Runs a request the way the API does: FormAccessBehavior first, then the handler.</summary>
    private static Task<T> Send<T>(World w, Caller caller, IRequest<T> request, Func<Task<T>> handler) =>
        new FormAccessBehavior<IRequest<T>, T>(w.Db, caller).Handle(request, () => handler(), CancellationToken.None);

    private static Task<RecordDetailDto> Detail(World w, Caller caller, Guid formId, Guid recordId)
    {
        var q = new GetRecordDetailQuery(formId, recordId);
        return Send(w, caller, q, () => new GetRecordDetailQueryHandler(w.Db, w.Repo, caller, TestPipeline.Providers()).Handle(q, CancellationToken.None));
    }

    private static Task<MyEntriesPageDto> Mine(World w, Caller caller, Guid? formId = null, string? before = null, int pageSize = 20)
    {
        var q = new GetMyEntriesQuery(formId, before, pageSize);
        return Send(w, caller, q, () => new GetMyEntriesQueryHandler(w.Db, w.Repo, caller, TestPipeline.Providers()).Handle(q, CancellationToken.None));
    }

    // ---------------------------------------------------------------- record detail

    [Fact]
    public async Task Detail_TheSenderGetsTheirRecordWithAttachments()
    {
        var w = await CreateAsync();
        var d = await Detail(w, w.PmCaller, w.Restricted.Id, w.PmRow.Id);
        d.Record.Values["crew_count"].Should().Be(10);
        d.Record.AttachmentCount.Should().Be(1);
        d.Attachments.Should().ContainSingle(a => a.OriginalFileName == "a.jpg");
    }

    [Fact]
    public async Task Detail_AUserWithoutFormAccessGets403_EvenForTheirOwnRecord()
    {
        var w = await CreateAsync();
        var act = () => Detail(w, w.OutsiderCaller, w.Restricted.Id, w.OutsiderRowOnRestricted.Id);
        await act.Should().ThrowAsync<ForbiddenAccessException>();
    }

    [Fact]
    public async Task Detail_SomeoneElsesRecordIs403_EvenWithFormAccess()
    {
        var w = await CreateAsync();
        var act = () => Detail(w, w.Pm2Caller, w.Restricted.Id, w.PmRow.Id);
        await act.Should().ThrowAsync<ForbiddenAccessException>();
    }

    [Fact]
    public async Task Detail_AnAdministratorOpensAnyRecord()
    {
        var w = await CreateAsync();
        (await Detail(w, w.AdminCaller, w.Restricted.Id, w.Pm2Row.Id)).Record.Id.Should().Be(w.Pm2Row.Id);
    }

    [Fact]
    public async Task Detail_AMissingRecordIs404()
    {
        var w = await CreateAsync();
        var act = () => Detail(w, w.AdminCaller, w.Restricted.Id, Guid.NewGuid());
        await act.Should().ThrowAsync<NotFoundException>();
    }

    // ---------------------------------------------------------------- my entries

    [Fact]
    public async Task Mine_AUserWithoutFormAccessGets403_ForThatForm()
    {
        var w = await CreateAsync();
        var act = () => Mine(w, w.OutsiderCaller, w.Restricted.Id);
        await act.Should().ThrowAsync<ForbiddenAccessException>();
    }

    [Fact]
    public async Task Mine_ListsOnlyOwnRecords_OnFormsTheCallerCanAccess()
    {
        var w = await CreateAsync();
        (await Mine(w, w.OutsiderCaller)).Items.Select(i => i.Id).Should().Equal(w.OutsiderRowOnOpen.Id);
        (await Mine(w, w.PmCaller)).Items.Select(i => i.Id).Should().Equal(w.PmRow.Id);
        w.Repo.LastSearch["Data_Restricted"].CreatedByUserId.Should().Be(w.Pm, "own rows are enforced in the query, not taken from the request");
    }

    [Fact]
    public async Task Mine_PagesNewestFirstWithACursor()
    {
        var w = await CreateAsync();
        var t = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);
        w.Repo.Records["Data_Open"].AddRange(Enumerable.Range(0, 24).Select(i => Row(w.Pm, t.AddHours(i), i)));
        var first = await Mine(w, w.PmCaller);
        first.Items.Should().HaveCount(20);
        first.Items.Select(i => i.CreatedAtUtc).Should().BeInDescendingOrder();
        first.NextBefore.Should().NotBeNull();
        var second = await Mine(w, w.PmCaller, before: first.NextBefore);
        second.Items.Should().HaveCount(5, "24 on the open form + 1 on the restricted one = 25");
        second.NextBefore.Should().BeNull();
        first.Items.Select(i => i.Id).Intersect(second.Items.Select(i => i.Id)).Should().BeEmpty();
    }

    [Fact]
    public async Task Mine_ABadCursorIs400()
    {
        var w = await CreateAsync();
        var act = () => Mine(w, w.PmCaller, before: "nonsense");
        await act.Should().ThrowAsync<ValidationException>();
    }

    // ---------------------------------------------------------------- records search

    [Fact]
    public async Task Search_PassesFiltersTextAndSaudiDaysToTheRepository()
    {
        var w = await CreateAsync();
        var q = new SearchRecordsQuery(w.Restricted.Id, Q: "late", From: "2026-11-01", To: "2026-11-07", Filters: new[] { "crew_count:10" });
        var page = await Send(w, w.AdminCaller, q, () => new SearchRecordsQueryHandler(w.Db, w.Repo, TestPipeline.Providers()).Handle(q, CancellationToken.None));
        var s = w.Repo.LastSearch["Data_Restricted"];
        s.Text.Should().Be("late");
        s.Matches!.Single().Should().Be(new Platform.Application.Common.Interfaces.DynamicRowFilter("crew_count", 10));
        s.From.Should().Be(new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.FromHours(3)));
        s.To.Should().Be(new DateTimeOffset(2026, 11, 8, 0, 0, 0, TimeSpan.FromHours(3)), "the end day is included");
        page.Items.Should().BeEmpty("no record matches 'late'");
    }

    [Theory]
    [InlineData("nope:1")]          // not a field
    [InlineData("crew_count:abc")]  // not a number
    public async Task Search_RefusesBadFilters(string filter)
    {
        var w = await CreateAsync();
        var q = new SearchRecordsQuery(w.Restricted.Id, Filters: new[] { filter });
        var act = () => new SearchRecordsQueryHandler(w.Db, w.Repo, TestPipeline.Providers()).Handle(q, CancellationToken.None);
        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task Search_ReturnsSenderNamesAndCounts()
    {
        var w = await CreateAsync();
        var user = User.Create("pm@example.com", "Hazim", "x", null);
        typeof(User).GetProperty("Id")!.SetValue(user, w.Pm);
        w.Db.Users.Add(user);
        await w.Db.SaveChangesAsync();
        var q = new SearchRecordsQuery(w.Restricted.Id);
        var page = await new SearchRecordsQueryHandler(w.Db, w.Repo, TestPipeline.Providers()).Handle(q, CancellationToken.None);
        var mine = page.Items.Single(i => i.Id == w.PmRow.Id);
        (mine.CreatedByName, mine.AttachmentCount).Should().Be(("Hazim", 1));
        page.TotalCount.Should().Be(3);
    }
}
