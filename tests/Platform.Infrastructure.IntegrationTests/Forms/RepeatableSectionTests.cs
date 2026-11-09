using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Platform.Application.Common.Interfaces;
using Platform.Application.Forms;
using Platform.Application.Forms.Commands.SubmitFormData;
using Platform.Domain.Forms;
using Platform.Domain.Forms.Enums;
using Platform.Infrastructure.IntegrationTests.Analytics;
using Platform.Infrastructure.Persistence;
using Xunit;
using AppValidation = Platform.Application.Common.Exceptions.ValidationException;

namespace Platform.Infrastructure.IntegrationTests.Forms;

/// <summary>
/// The generic form-engine pieces added for stage progress (2026-11-10): repeatable sections
/// validated per block, multi-select, calculated fields, options providers and visibility by
/// an attribute of the chosen option, and the hook that runs under the write's lock.
/// </summary>
public class RepeatableSectionTests
{
    private const string Opts = """[{"value":"a","label":"A"},{"value":"b","label":"B"},{"value":"c","label":"C"}]""";

    /// <summary>Stage-like options: "slab" is reported per floor, "permit" for the whole project.</summary>
    private sealed class FakeStages : IFieldOptionsProvider
    {
        public string Key => "test.stages";
        public string Description => "test";
        public Task<IReadOnlyList<FieldOption>> GetOptionsAsync(FieldOptionsContext context, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<FieldOption>>(
            [
                new("slab", "بلاطة", "Slab", new Dictionary<string, string> { ["unit_pick"] = "floors" }),
                new("permit", "رخصة", "Permit", new Dictionary<string, string> { ["unit_pick"] = "none" }),
            ]);
    }

    private sealed record World(ApplicationDbContext Db, FakeDynamicDataRepository Repo, FormDefinition Form, FormSection Section);

    private static async Task<World> CreateAsync()
    {
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var form = FormDefinition.Create("report", "تقرير", "Ops", null);
        var v = form.GetDraftVersion();
        v.AddField("report_date", "التاريخ", FieldType.DateTime, true, null, null, null);
        v.AddField("plot", "الأرض", FieldType.Decimal, false, null, null, null);
        v.AddField("pct", "النسبة", FieldType.Decimal, false, null, null, null);
        var area = v.AddField("area", "المساحة", FieldType.Decimal, false, null, null, null);
        area.SetFormula("plot * pct / 100");
        var section = v.AddSection("stages", "المراحل", "Stages", 1, 3);
        v.AddField("stage", "المرحلة", FieldType.Dropdown, true, null, null, null, sectionId: section.Id, optionsProvider: "test.stages");
        var floors = v.AddField("floors", "الأدوار", FieldType.MultiSelect, true, Opts, null, null, "stage", "[\"floors\"]", sectionId: section.Id);
        floors.SetVisibilityAttribute("unit_pick");
        v.AddField("plan_met", "تم؟", FieldType.Boolean, true, null, null, null, sectionId: section.Id);
        v.AddField("has_problem", "مشكلة؟", FieldType.Boolean, false, null, null, null, sectionId: section.Id);
        v.AddField("problem", "الوصف", FieldType.LongText, true, null, null, null, "has_problem", "[\"true\"]", sectionId: section.Id);
        v.MarkPublished();
        form.MarkPublished(v, "Data_Report");
        section.AssignChildTable("Data_Report__Stages");
        db.FormDefinitions.Add(form);
        await db.SaveChangesAsync();
        return new World(db, new FakeDynamicDataRepository(), form, section);
    }

    private static IReadOnlyDictionary<string, object?> Values(object o) =>
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(JsonSerializer.Serialize(o))!.ToDictionary(kv => kv.Key, kv => (object?)kv.Value);

    private static Task<PreparedSubmission> Prepare(World w, object values) =>
        TestPipeline.Preparer(w.Db, w.Repo, new FakeStages()).PrepareAsync(w.Form, w.Form.GetPublishedVersion()!, Values(values), CancellationToken.None);

    private static readonly object Slab = new { stage = "slab", floors = new[] { "a", "b" }, plan_met = true, has_problem = false };
    private static readonly object Permit = new { stage = "permit", plan_met = false, has_problem = false };

    [Fact]
    public async Task TwoValidBlocks_AreNormalized_InOrder()
    {
        var w = await CreateAsync();
        var p = await Prepare(w, new { report_date = "2026-11-08", stages = new[] { Slab, Permit } });
        var blocks = p.BlocksOf("stages");
        blocks.Should().HaveCount(2);
        blocks[0]["stage"].Should().Be("slab");
        blocks[0]["floors"].Should().BeEquivalentTo(new[] { "a", "b" });
        blocks[1]["stage"].Should().Be("permit");
        blocks[1]["floors"].Should().BeNull();
        p.Header.Should().NotContainKey("stages");
    }

    [Fact]
    public async Task ARequiredFieldMissingInOneBlock_IsReportedForThatBlock()
    {
        var w = await CreateAsync();
        var act = () => Prepare(w, new { report_date = "2026-11-08", stages = new object[] { Slab, new { stage = "permit", has_problem = false } } });
        (await act.Should().ThrowAsync<AppValidation>()).Which.Errors.Should().ContainKey("stages[1].plan_met").And.HaveCount(1);
    }

    [Fact]
    public async Task VisibilityInsideABlock_IsEvaluatedPerBlock()
    {
        var w = await CreateAsync();
        // Block 0 has a problem and no description (required there); block 1 has none (not required).
        var act = () => Prepare(w, new
        {
            report_date = "2026-11-08",
            stages = new object[]
            {
                new { stage = "permit", plan_met = true, has_problem = true },
                new { stage = "permit", plan_met = true, has_problem = false },
            },
        });
        (await act.Should().ThrowAsync<AppValidation>()).Which.Errors.Keys.Should().BeEquivalentTo("stages[0].problem");
    }

    [Fact]
    public async Task AFieldShownByTheChosenOptionsAttribute_IsRequiredOnlyWhenShown()
    {
        var w = await CreateAsync();
        // slab -> unit_pick=floors -> floors shown and required; permit -> hidden.
        var act = () => Prepare(w, new { report_date = "2026-11-08", stages = new object[] { new { stage = "slab", plan_met = true }, Permit } });
        (await act.Should().ThrowAsync<AppValidation>()).Which.Errors.Keys.Should().BeEquivalentTo("stages[0].floors");
    }

    [Fact]
    public async Task TooFewOrTooManyBlocks_AreRefused()
    {
        var w = await CreateAsync();
        (await ((Func<Task>)(() => Prepare(w, new { report_date = "2026-11-08", stages = Array.Empty<object>() })))
            .Should().ThrowAsync<AppValidation>()).Which.Errors.Should().ContainKey("stages");
        (await ((Func<Task>)(() => Prepare(w, new { report_date = "2026-11-08", stages = new[] { Permit, Permit, Permit, Permit } })))
            .Should().ThrowAsync<AppValidation>()).Which.Errors.Should().ContainKey("stages");
    }

    [Fact]
    public async Task AChoiceTheProviderDoesNotOffer_IsRefused()
    {
        var w = await CreateAsync();
        var act = () => Prepare(w, new { report_date = "2026-11-08", stages = new[] { new { stage = "roofing", plan_met = true, has_problem = false } } });
        (await act.Should().ThrowAsync<AppValidation>()).Which.Errors.Should().ContainKey("stages[0].stage");
    }

    [Fact]
    public async Task AMultiSelectOptionNotInTheList_IsRefused_AndRepeatsAreDropped()
    {
        var w = await CreateAsync();
        var bad = () => Prepare(w, new { report_date = "2026-11-08", stages = new[] { new { stage = "slab", floors = new[] { "a", "zzz" }, plan_met = true } } });
        (await bad.Should().ThrowAsync<AppValidation>()).Which.Errors.Should().ContainKey("stages[0].floors");

        var p = await Prepare(w, new { report_date = "2026-11-08", stages = new[] { new { stage = "slab", floors = new[] { "a", "a", "b" }, plan_met = true } } });
        p.BlocksOf("stages")[0]["floors"].Should().BeEquivalentTo(new[] { "a", "b" });
    }

    [Fact]
    public async Task ACalculatedField_IsComputedByTheServer_WhateverWasSent()
    {
        var w = await CreateAsync();
        var p = await Prepare(w, new { report_date = "2026-11-08", plot = 600, pct = 75, area = 1, stages = new[] { Permit } });
        p.Header["area"].Should().Be(450m);
        var missing = await Prepare(w, new { report_date = "2026-11-08", plot = 600, stages = new[] { Permit } });
        missing.Header["area"].Should().BeNull();
    }

    [Fact]
    public async Task ArabicDigitsAndOversizedText_InABlock_AreCheckedLikeAnywhereElse()
    {
        var w = await CreateAsync();
        var act = () => Prepare(w, new
        {
            report_date = "2026-11-08",
            stages = new[] { new { stage = "permit", plan_met = true, has_problem = true, problem = (string?)null } },
        });
        (await act.Should().ThrowAsync<AppValidation>()).Which.Errors.Should().ContainKey("stages[0].problem");
        var blank = () => Prepare(w, new { report_date = "2026-11-08", stages = new[] { new { stage = "permit", plan_met = true, has_problem = true, problem = "   " } } });
        (await blank.Should().ThrowAsync<AppValidation>()).Which.Errors.Should().ContainKey("stages[0].problem");
    }

    [Fact]
    public async Task Submit_WritesTheBlocks_AndAHookCanRefuseUnderTheLock()
    {
        var w = await CreateAsync();
        var refuse = new RefusingHook();
        Task<Guid> Submit(params IFormSubmissionHook[] hooks) =>
            new SubmitFormDataCommandHandler(w.Db, w.Repo, new TestPipeline.Caller(), TestPipeline.Preparer(w.Db, w.Repo, new FakeStages()), TestPipeline.Hooks(hooks))
                .Handle(new SubmitFormDataCommand(w.Form.Id, Values(new { report_date = "2026-11-08", stages = new[] { Slab, Permit } }), Guid.NewGuid()), CancellationToken.None);

        var id = await Submit();
        w.Repo.Blocks["Data_Report__Stages"].Where(b => b.ParentId == id).Should().HaveCount(2);

        var act = () => Submit(refuse);
        await act.Should().ThrowAsync<AppValidation>().WithMessage("*refused*");
        w.Repo.LockKeys.Last().Should().Be("lock:report");
        w.Repo.Records["Data_Report"].Should().HaveCount(1, "the refused submission wrote nothing");
    }

    private sealed class RefusingHook : IFormSubmissionHook
    {
        public string FormCode => "report";
        public string? LockKey(SubmissionHookContext context) => "lock:report";
        public Task ValidateAsync(SubmissionHookContext context, CancellationToken cancellationToken) =>
            throw new AppValidation("test.refused", "refused by the hook");
    }
}
