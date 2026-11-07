using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Platform.Application.Common.Exceptions;
using Platform.Application.Common.Interfaces;
using Platform.Application.Forms.Commands.AddFieldDefinition;
using Platform.Application.Forms.Commands.FormSteps;
using Platform.Application.Forms.Commands.StartNewFormVersion;
using Platform.Application.Forms.Commands.UpdateFieldLabel;
using Platform.Application.Forms.Commands.UpdateFieldOptionLabels;
using Platform.Application.Forms.Commands.UpdateFormName;
using Platform.Application.Forms.Queries.GetFormDefinition;
using Platform.Domain.Forms;
using Platform.Domain.Forms.Enums;
using Platform.Infrastructure.Persistence;
using Xunit;

namespace Platform.Infrastructure.IntegrationTests.Forms;

/// <summary>
/// Redesign phase 6 (HANDOFF §9.1 bilingual labels, §9.2 steps): metadata-only edits on a live
/// form, steps surviving a new version, and nothing physical being touched.
/// </summary>
public class FormStepsAndEnglishLabelsTests
{
    private const string PhaseOptions =
        "[{\"value\":\"excavation\",\"label\":\"الحفر\"},{\"value\":\"foundation\",\"label\":\"الأساسات\"}]";

    private static ApplicationDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed record Setup(ApplicationDbContext Db, FormDefinition Form, FieldDefinition Date, FieldDefinition Crew, FieldDefinition Phase, FieldDefinition Photo);

    private static async Task<Setup> LiveFormAsync()
    {
        var db = CreateContext();
        var form = FormDefinition.Create("daily-report", "تقرير يومي", "Daily Reports", null);
        var v = form.GetDraftVersion();
        var date = v.AddField("report_date", "تاريخ التقرير", FieldType.DateTime, true, null, null, null);
        var crew = v.AddField("crew_count", "عدد العمالة", FieldType.Number, true, null, null, null);
        var phase = v.AddField("phase", "المرحلة", FieldType.Dropdown, true, PhaseOptions, null, null);
        var photo = v.AddField("site_photo", "صورة", FieldType.Attachment, false, null, null, null);
        v.MarkPublished();
        form.MarkPublished(v, "Data_DailyReport");
        db.FormDefinitions.Add(form);
        await db.SaveChangesAsync();
        return new Setup(db, form, date, crew, phase, photo);
    }

    private sealed class Admin : ICurrentUserService
    {
        public Guid? UserId { get; } = Guid.NewGuid();
        public string? Email => null;
        public IReadOnlyCollection<string> Roles => new[] { "Administrator" };
        public bool IsAuthenticated => true;
    }

    private static async Task<Platform.Application.Forms.Dtos.FormDefinitionDto> Read(Setup s)
    {
        s.Db.ChangeTracker.Clear();   // read back what was saved, not the tracked objects
        return await new GetFormDefinitionQueryHandler(s.Db, new Admin()).Handle(new GetFormDefinitionQuery(s.Form.Id), CancellationToken.None);
    }

    // ---------------------------------------------------------------- names and labels

    [Fact]
    public async Task FormName_SetsArabicAndEnglish_AndNeverTheTableName()
    {
        var s = await LiveFormAsync();
        await new UpdateFormNameCommandHandler(s.Db).Handle(new UpdateFormNameCommand(s.Form.Id, " تقرير التقدم ", " Daily progress "), default);

        var dto = await Read(s);
        dto.Name.Should().Be("تقرير التقدم");
        dto.NameEn.Should().Be("Daily progress");
        dto.TableName.Should().Be("Data_DailyReport");
    }

    [Fact]
    public async Task FieldLabel_NullEnglishLeavesItAlone_EmptyClearsIt()
    {
        var s = await LiveFormAsync();
        var handler = new UpdateFieldLabelCommandHandler(s.Db, new FakeDynamicSchemaService());

        await handler.Handle(new UpdateFieldLabelCommand(s.Form.Id, s.Crew.Id, "عدد العمالة", "Workers"), default);
        await handler.Handle(new UpdateFieldLabelCommand(s.Form.Id, s.Crew.Id, "العمالة الحاضرة"), default);
        (await Read(s)).PublishedVersion!.Fields.Single(f => f.Code == "crew_count").LabelEn.Should().Be("Workers");

        await handler.Handle(new UpdateFieldLabelCommand(s.Form.Id, s.Crew.Id, "العمالة الحاضرة", ""), default);
        (await Read(s)).PublishedVersion!.Fields.Single(f => f.Code == "crew_count").LabelEn.Should().BeNull();
    }

    [Fact]
    public async Task OptionLabels_RelabelAndReorder_KeepEveryValue()
    {
        var s = await LiveFormAsync();
        await new UpdateFieldOptionLabelsCommandHandler(s.Db).Handle(new UpdateFieldOptionLabelsCommand(s.Form.Id, s.Phase.Id, new[]
        {
            new OptionLabelDto("foundation", "الأساسات", "Foundation"),
            new OptionLabelDto("excavation", "الحفر", "Excavation"),
        }), default);

        var json = (await Read(s)).PublishedVersion!.Fields.Single(f => f.Code == "phase").OptionsJson;
        json.Should().Be("[{\"value\":\"foundation\",\"label\":\"الأساسات\",\"labelEn\":\"Foundation\"},{\"value\":\"excavation\",\"label\":\"الحفر\",\"labelEn\":\"Excavation\"}]");
    }

    [Theory]
    [InlineData("excavation", "foundation", "piling")]   // adds a value
    [InlineData("excavation", null, null)]                // drops a value
    [InlineData("excavation", "Foundation", null)]        // changes a value (case counts)
    [InlineData("excavation", "excavation", null)]        // sends one twice
    public async Task OptionLabels_RefuseAnyChangeToTheValues(string a, string? b, string? c)
    {
        var s = await LiveFormAsync();
        var options = new[] { a, b, c }.Where(v => v is not null).Select(v => new OptionLabelDto(v!, "x", null)).ToList();

        var act = () => new UpdateFieldOptionLabelsCommandHandler(s.Db).Handle(new UpdateFieldOptionLabelsCommand(s.Form.Id, s.Phase.Id, options), default);

        await act.Should().ThrowAsync<ValidationException>();
        s.Phase.OptionsJson.Should().Be(PhaseOptions);
    }

    [Fact]
    public async Task OptionLabels_OnlyForAStaticDropdown()
    {
        var s = await LiveFormAsync();
        var act = () => new UpdateFieldOptionLabelsCommandHandler(s.Db).Handle(
            new UpdateFieldOptionLabelsCommand(s.Form.Id, s.Crew.Id, new[] { new OptionLabelDto("1", "x", null) }), default);
        await act.Should().ThrowAsync<ValidationException>();
    }

    // ---------------------------------------------------------------- steps

    private static async Task<(Guid basics, Guid progress, Guid photos)> ThreeStepsAsync(Setup s)
    {
        var add = new AddFormStepCommandHandler(s.Db);
        var basics = await add.Handle(new AddFormStepCommand(s.Form.Id, "الأساسيات", "Basics"), default);
        var progress = await add.Handle(new AddFormStepCommand(s.Form.Id, "التقدم", "Progress"), default);
        var photos = await add.Handle(new AddFormStepCommand(s.Form.Id, "الصور", null), default);
        var move = new SetFieldStepCommandHandler(s.Db);
        await move.Handle(new SetFieldStepCommand(s.Form.Id, s.Date.Id, basics.Id), default);
        await move.Handle(new SetFieldStepCommand(s.Form.Id, s.Crew.Id, basics.Id), default);
        await move.Handle(new SetFieldStepCommand(s.Form.Id, s.Phase.Id, progress.Id), default);
        await move.Handle(new SetFieldStepCommand(s.Form.Id, s.Photo.Id, photos.Id), default);
        return (basics.Id, progress.Id, photos.Id);
    }

    [Fact]
    public async Task Steps_AreAddedInOrder_AndFieldsPointAtThem()
    {
        var s = await LiveFormAsync();
        var (basics, progress, photos) = await ThreeStepsAsync(s);

        var v = (await Read(s)).PublishedVersion!;
        v.Steps!.Select(x => (x.TitleAr, x.TitleEn, x.DisplayOrder)).Should().Equal(("الأساسيات", "Basics", 0), ("التقدم", "Progress", 1), ("الصور", null, 2));
        v.Fields.Single(f => f.Code == "phase").StepId.Should().Be(progress);
        v.Fields.Single(f => f.Code == "site_photo").StepId.Should().Be(photos);
        _ = basics;
    }

    [Fact]
    public async Task Steps_RenameAndReorder()
    {
        var s = await LiveFormAsync();
        var (basics, progress, photos) = await ThreeStepsAsync(s);

        await new UpdateFormStepCommandHandler(s.Db).Handle(new UpdateFormStepCommand(s.Form.Id, photos, "الصور", "Photos"), default);
        await new ReorderFormStepsCommandHandler(s.Db).Handle(new ReorderFormStepsCommand(s.Form.Id, new[] { photos, basics, progress }), default);

        (await Read(s)).PublishedVersion!.Steps!.Select(x => x.TitleEn).Should().Equal("Photos", "Basics", "Progress");

        var partial = () => new ReorderFormStepsCommandHandler(s.Db).Handle(new ReorderFormStepsCommand(s.Form.Id, new[] { photos, basics }), default);
        await partial.Should().ThrowAsync<ValidationException>("every step must be listed exactly once");
    }

    [Fact]
    public async Task DeletingAStep_MovesItsFieldsToThePreviousStep()
    {
        var s = await LiveFormAsync();
        var (basics, progress, _) = await ThreeStepsAsync(s);

        await new DeleteFormStepCommandHandler(s.Db).Handle(new DeleteFormStepCommand(s.Form.Id, progress), default);

        var v = (await Read(s)).PublishedVersion!;
        v.Steps!.Select(x => (x.TitleAr, x.DisplayOrder)).Should().Equal(("الأساسيات", 0), ("الصور", 1));
        v.Fields.Single(f => f.Code == "phase").StepId.Should().Be(basics, "no field is lost when its step goes");
    }

    [Fact]
    public async Task DeletingTheFirstStep_MovesItsFieldsToTheNextOne_AndTheOnlyStep_LeavesThemStepless()
    {
        var s = await LiveFormAsync();
        var (basics, progress, photos) = await ThreeStepsAsync(s);
        var delete = new DeleteFormStepCommandHandler(s.Db);

        await delete.Handle(new DeleteFormStepCommand(s.Form.Id, basics), default);
        (await Read(s)).PublishedVersion!.Fields.Single(f => f.Code == "crew_count").StepId.Should().Be(progress);

        await delete.Handle(new DeleteFormStepCommand(s.Form.Id, progress), default);
        await delete.Handle(new DeleteFormStepCommand(s.Form.Id, photos), default);
        var v = (await Read(s)).PublishedVersion!;
        v.Steps.Should().BeEmpty();
        v.Fields.Should().OnlyContain(f => f.StepId == null);
    }

    [Fact]
    public async Task AStepOfAnotherForm_IsRefused()
    {
        var s = await LiveFormAsync();
        var act = () => new SetFieldStepCommandHandler(s.Db).Handle(new SetFieldStepCommand(s.Form.Id, s.Crew.Id, Guid.NewGuid()), default);
        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task AddField_CanGoStraightIntoAStep_WithAnEnglishLabel()
    {
        var s = await LiveFormAsync();
        var (_, progress, _) = await ThreeStepsAsync(s);
        var schema = new FakeDynamicSchemaService();

        var id = await new AddFieldDefinitionCommandHandler(s.Db, schema).Handle(new AddFieldDefinitionCommand(
            s.Form.Id, "extra_photo", "صورة إضافية", FieldType.Attachment, false, null, null, null, LabelEn: "Extra photo", StepId: progress), default);

        var f = (await Read(s)).PublishedVersion!.Fields.Single(x => x.Id == id);
        (f.LabelEn, f.StepId).Should().Be(("Extra photo", progress));

        var wrong = () => new AddFieldDefinitionCommandHandler(s.Db, schema).Handle(new AddFieldDefinitionCommand(
            s.Form.Id, "notes2", "x", FieldType.ShortText, false, null, null, null, StepId: Guid.NewGuid()), default);
        await wrong.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task ANewVersion_CarriesTheStepsAndEnglishLabels_WithFieldsInTheirCopiedSteps()
    {
        var s = await LiveFormAsync();
        var (_, progress, _) = await ThreeStepsAsync(s);
        await new UpdateFieldLabelCommandHandler(s.Db, new FakeDynamicSchemaService())
            .Handle(new UpdateFieldLabelCommand(s.Form.Id, s.Phase.Id, "المرحلة", "Phase"), default);

        await new StartNewFormVersionCommandHandler(s.Db).Handle(new StartNewFormVersionCommand(s.Form.Id), default);

        var draft = (await Read(s)).DraftVersion!;
        draft.Steps!.Select(x => x.TitleAr).Should().Equal("الأساسيات", "التقدم", "الصور");
        draft.Steps!.Select(x => x.Id).Should().NotContain(progress, "the copy gets its own step rows");
        var phase = draft.Fields.Single(f => f.Code == "phase");
        phase.LabelEn.Should().Be("Phase");
        phase.StepId.Should().Be(draft.Steps!.Single(x => x.TitleAr == "التقدم").Id);
    }

    [Fact]
    public void NoStepEdit_TouchesAnythingPhysical()
    {
        // Every step/label command takes only the DbContext - none of them is handed the
        // schema service, so none of them can run DDL against the data table or its view.
        foreach (var handler in new[] { typeof(AddFormStepCommandHandler), typeof(UpdateFormStepCommandHandler), typeof(ReorderFormStepsCommandHandler),
                     typeof(DeleteFormStepCommandHandler), typeof(SetFieldStepCommandHandler), typeof(UpdateFormNameCommandHandler), typeof(UpdateFieldOptionLabelsCommandHandler) })
            handler.GetConstructors().Single().GetParameters().Select(p => p.ParameterType.Name).Should().Equal("IApplicationDbContext");
    }
}
