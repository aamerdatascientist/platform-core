using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Platform.Application.Common.Interfaces;
using Platform.Application.Forms.Commands.ArchiveField;
using Platform.Application.Forms.Commands.DeleteSubmission;
using Platform.Application.Forms.Commands.UpdateFieldRules;
using Platform.Application.Forms.Commands.UpdateFieldVisibility;
using Platform.Application.Forms.Commands.UpdateSubmission;
using Platform.Domain.Forms;
using Platform.Domain.Forms.Enums;
using Platform.Infrastructure.IntegrationTests.Analytics;
using Platform.Infrastructure.Persistence;
using Xunit;
using AppValidationException = Platform.Application.Common.Exceptions.ValidationException;
using NotFoundException = Platform.Application.Common.Exceptions.NotFoundException;

namespace Platform.Infrastructure.IntegrationTests.Forms;

/// <summary>
/// Changing a live field's rules, and correcting or removing submitted records - the commands
/// added in the 2026-10-20 hardening batch.
/// </summary>
public class FieldRulesAndRecordEditingTests
{
    private const string Table = "Data_DailyReport";

    private static ApplicationDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed record Setup(ApplicationDbContext Db, FormDefinition Form, FieldDefinition Crew, FieldDefinition Zone, FieldDefinition Date, FieldDefinition Notes);

    private static async Task<Setup> CreateLiveFormAsync()
    {
        var db = CreateContext();
        var form = FormDefinition.Create("daily-report", "Daily report", "Daily Reports", null);
        var v = form.GetDraftVersion();
        var crew = v.AddField("crew_count", "Crew", FieldType.Number, true, null, null, null);
        var zone = v.AddField("zone", "Zone", FieldType.ShortText, false, null, null, null);
        var date = v.AddField("report_date", "Date", FieldType.DateTime, true, null, null, null);
        var notes = v.AddField("notes", "Notes", FieldType.LongText, true, null, null, null);
        v.MarkPublished();
        form.MarkPublished(v, Table);
        db.FormDefinitions.Add(form);
        await db.SaveChangesAsync();
        return new Setup(db, form, crew, zone, date, notes);
    }

    private static Dictionary<string, object?> Values(object anonymous) =>
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(JsonSerializer.Serialize(anonymous))!
            .ToDictionary(kv => kv.Key, kv => (object?)kv.Value);

    [Fact]
    public async Task UpdateFieldRules_MakesAnOptionalFieldRequired_WithoutTouchingTheColumn()
    {
        var s = await CreateLiveFormAsync();
        var schema = new FakeDynamicSchemaService();

        await new UpdateFieldRulesCommandHandler(s.Db, schema).Handle(
            new UpdateFieldRulesCommand(s.Form.Id, s.Zone.Id, true, null, null, false, null, false), CancellationToken.None);

        s.Zone.IsRequired.Should().BeTrue();
        schema.DroppedNotNull.Should().BeEmpty("a live column is never made NOT NULL, and an optional one has none to drop");
    }

    [Fact]
    public async Task UpdateFieldRules_TurningRequiredOff_DropsNotNullOnTheLiveColumn()
    {
        var s = await CreateLiveFormAsync();
        var schema = new FakeDynamicSchemaService();

        await new UpdateFieldRulesCommandHandler(s.Db, schema).Handle(
            new UpdateFieldRulesCommand(s.Form.Id, s.Crew.Id, false, 0, null, false, null, false), CancellationToken.None);

        s.Crew.IsRequired.Should().BeFalse();
        schema.DroppedNotNull.Should().Equal((Table, "crew_count"));
    }

    [Fact]
    public async Task UpdateFieldRules_KeepsOnlyTheRulesThatFitTheFieldType()
    {
        var s = await CreateLiveFormAsync();
        var handler = new UpdateFieldRulesCommandHandler(s.Db, new FakeDynamicSchemaService());

        await handler.Handle(new UpdateFieldRulesCommand(s.Form.Id, s.Crew.Id, true, 0, 500, true, "abc", true), CancellationToken.None);
        await handler.Handle(new UpdateFieldRulesCommand(s.Form.Id, s.Date.Id, true, 1, 2, false, null, true), CancellationToken.None);
        await handler.Handle(new UpdateFieldRulesCommand(s.Form.Id, s.Notes.Id, true, null, null, false, "   ", false), CancellationToken.None);

        s.Crew.ValidationRulesJson.Should().Be("""{"min":0,"max":500,"minExclusive":true}""");
        s.Date.ValidationRulesJson.Should().Be("""{"noFutureDates":true}""");
        s.Notes.ValidationRulesJson.Should().BeNull();
    }

    [Fact]
    public async Task UpdateFieldRules_RejectsAnInvalidPattern()
    {
        var s = await CreateLiveFormAsync();
        var act = () => new UpdateFieldRulesCommandHandler(s.Db, new FakeDynamicSchemaService()).Handle(
            new UpdateFieldRulesCommand(s.Form.Id, s.Notes.Id, true, null, null, false, "([", false), CancellationToken.None);

        await act.Should().ThrowAsync<AppValidationException>();
    }

    [Fact]
    public async Task ArchivingALiveField_DropsItsNotNull_SoLaterSubmissionsStillSave()
    {
        var s = await CreateLiveFormAsync();
        var schema = new FakeDynamicSchemaService();

        await new ArchiveFieldCommandHandler(s.Db, schema).Handle(new ArchiveFieldCommand(s.Form.Id, s.Notes.Id), CancellationToken.None);

        s.Notes.IsActive.Should().BeFalse();
        schema.DroppedNotNull.Should().Equal((Table, "notes"));
    }

    [Fact]
    public async Task MakingALiveFieldConditional_DropsItsNotNull()
    {
        var s = await CreateLiveFormAsync();
        var schema = new FakeDynamicSchemaService();

        await new UpdateFieldVisibilityCommandHandler(s.Db, schema).Handle(
            new UpdateFieldVisibilityCommand(s.Form.Id, s.Notes.Id, "zone", """["a"]"""), CancellationToken.None);

        schema.DroppedNotNull.Should().Equal((Table, "notes"));
    }

    [Fact]
    public async Task UpdateSubmission_ValidatesTrimsAndSaves()
    {
        var s = await CreateLiveFormAsync();
        var recordId = Guid.NewGuid();
        var editor = Guid.NewGuid();
        var data = new FakeDynamicDataRepository();
        data.Rows[Table] = new List<DynamicRow> { new(recordId, new Dictionary<string, object?> { ["crew_count"] = 3 }) };
        var handler = new UpdateSubmissionCommandHandler(s.Db, data);

        await handler.Handle(
            new UpdateSubmissionCommand(s.Form.Id, recordId, Values(new { crew_count = 7, report_date = "2026-10-01", notes = "  fixed  " }), editor),
            CancellationToken.None);

        var row = data.Rows[Table].Single();
        row.Values["notes"].Should().Be("fixed");
        row.Values["zone"].Should().BeNull("a field not sent is cleared - the body is the whole record");
        data.Modifications.Last().Should().Be((Table, recordId, editor));
    }

    [Fact]
    public async Task UpdateSubmission_RefusesAMissingRequiredValue_AndAnUnknownRecord()
    {
        var s = await CreateLiveFormAsync();
        var data = new FakeDynamicDataRepository();
        data.Rows[Table] = new List<DynamicRow>();
        var handler = new UpdateSubmissionCommandHandler(s.Db, data);

        var missing = () => handler.Handle(
            new UpdateSubmissionCommand(s.Form.Id, Guid.NewGuid(), Values(new { crew_count = 1, report_date = "2026-10-01", notes = " " }), Guid.NewGuid()),
            CancellationToken.None);
        await missing.Should().ThrowAsync<AppValidationException>();

        var unknown = () => handler.Handle(
            new UpdateSubmissionCommand(s.Form.Id, Guid.NewGuid(), Values(new { crew_count = 1, report_date = "2026-10-01", notes = "ok" }), Guid.NewGuid()),
            CancellationToken.None);
        await unknown.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task DeleteSubmission_RemovesTheRecord_AndA404AfterThat()
    {
        var s = await CreateLiveFormAsync();
        var recordId = Guid.NewGuid();
        var data = new FakeDynamicDataRepository();
        data.Rows[Table] = new List<DynamicRow> { new(recordId, new Dictionary<string, object?>()) };
        var handler = new DeleteSubmissionCommandHandler(s.Db, data);

        await handler.Handle(new DeleteSubmissionCommand(s.Form.Id, recordId, Guid.NewGuid()), CancellationToken.None);
        data.Rows[Table].Should().BeEmpty();

        var again = () => handler.Handle(new DeleteSubmissionCommand(s.Form.Id, recordId, Guid.NewGuid()), CancellationToken.None);
        await again.Should().ThrowAsync<NotFoundException>();
    }
}
