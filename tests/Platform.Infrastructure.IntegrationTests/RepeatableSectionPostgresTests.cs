using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Platform.Application.Common.Interfaces;
using Platform.Domain.Forms;
using Platform.Domain.Forms.Enums;
using Platform.Infrastructure.Persistence.DynamicSchema;
using Xunit;

namespace Platform.Infrastructure.IntegrationTests;

/// <summary>
/// Repeatable sections against a real Postgres (2026-11-10): the child table, array columns,
/// one transaction for parent and blocks, replacing blocks on edit, the per-block reporting
/// view with header columns and lookup names, and the advisory lock that serializes checks.
/// </summary>
public class RepeatableSectionPostgresTests : IAsyncLifetime
{
    private TestPostgres _postgres = default!;
    private DynamicSchemaService _schema = default!;
    private DynamicDataRepository _data = default!;
    private readonly Guid _user = Guid.NewGuid();

    public async Task InitializeAsync()
    {
        _postgres = await TestPostgres.StartAsync();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:PostgresConnection"] = _postgres.ConnectionString })
            .Build();
        _schema = new DynamicSchemaService(configuration, NullLogger<DynamicSchemaService>.Instance);
        _data = new DynamicDataRepository(configuration);
        await Exec("""CREATE TABLE "Users" ("Id" uuid PRIMARY KEY, "DisplayName" varchar(200));""");
        await Exec($"""INSERT INTO "Users" VALUES ('{_user}', 'Foreman');""");
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    private async Task Exec(string sql)
    {
        await using var c = new NpgsqlConnection(_postgres.ConnectionString);
        await c.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, c);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<T?> Scalar<T>(string sql)
    {
        await using var c = new NpgsqlConnection(_postgres.ConnectionString);
        await c.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, c);
        return (T?)await cmd.ExecuteScalarAsync();
    }

    private sealed record Built(FormDefinition Form, FormVersion Version, FormSection Section, FormDefinition Floors, Guid FloorA, Guid FloorB);

    private async Task<Built> BuildAsync()
    {
        // A floors master form with two rows, for the MultiLookup.
        var floors = FormDefinition.Create("floors", "الأدوار", "Ops", null);
        var fv = floors.GetDraftVersion();
        fv.AddField("label", "الاسم", FieldType.ShortText, true, null, null, null);
        fv.MarkPublished();
        floors.MarkPublished(fv, await _schema.EnsureTableForPublishedVersionAsync(floors, fv));
        var floorA = await _data.InsertAsync(floors.TableName!, fv.Fields, new Dictionary<string, object?> { ["label"] = "الدور 1" }, _user);
        var floorB = await _data.InsertAsync(floors.TableName!, fv.Fields, new Dictionary<string, object?> { ["label"] = "الدور 2" }, _user);

        var form = FormDefinition.Create("site-report", "تقرير", "Ops", null);
        var v = form.GetDraftVersion();
        v.AddField("report_date", "التاريخ", FieldType.DateTime, true, null, null, null);
        v.AddField("crew", "العمالة", FieldType.Number, true, null, null, null);
        var section = v.AddSection("stages", "المراحل", "Stages", 1, 10);
        v.AddField("stage", "المرحلة", FieldType.Dropdown, true, """[{"value":"walls","label":"الجدران"}]""", null, null, sectionId: section.Id);
        v.AddField("floors", "الأدوار", FieldType.MultiLookup, false, null, floors.Id, null, sectionId: section.Id);
        v.AddField("sides", "الواجهات", FieldType.MultiSelect, false, """[{"value":"1","label":"1"},{"value":"2","label":"2"}]""", null, null, sectionId: section.Id);
        v.AddField("plan_met", "تم؟", FieldType.Boolean, true, null, null, null, sectionId: section.Id);
        v.MarkPublished();
        form.MarkPublished(v, await _schema.EnsureTableForPublishedVersionAsync(form, v));
        await _schema.RefreshReportingViewAsync(form, v, new Dictionary<Guid, FormDefinition> { [floors.Id] = floors });
        return new Built(form, v, section, floors, floorA, floorB);
    }

    private static Dictionary<string, object?> Block(string stage, Guid[]? floors, string[]? sides, bool? met) => new()
    {
        ["stage"] = stage, ["floors"] = floors, ["sides"] = sides, ["plan_met"] = met,
    };

    private static IReadOnlyList<SectionRows> Rows(Built b, params Dictionary<string, object?>[] blocks) =>
        [new SectionRows(b.Section.ChildTableName!, b.Version.Fields.Where(f => f.SectionId == b.Section.Id).ToList(), blocks)];

    private static Dictionary<string, object?> Header(int crew) =>
        new() { ["report_date"] = new DateTimeOffset(2026, 11, 8, 0, 0, 0, TimeSpan.FromHours(3)), ["crew"] = crew };

    [Fact]
    public async Task Publishing_CreatesTheChildTable_WithArrayColumns_AndTheSectionKeepsItsName()
    {
        var b = await BuildAsync();
        b.Section.ChildTableName.Should().Be("Data_SiteReport__Stages");
        (await _schema.ColumnExistsAsync("Data_SiteReport", "stage")).Should().BeFalse("block fields live in the child table only");
        (await Scalar<string>("""SELECT data_type FROM information_schema.columns WHERE table_name='Data_SiteReport__Stages' AND column_name='floors'""")).Should().Be("ARRAY");
        (await Scalar<string>("""SELECT udt_name FROM information_schema.columns WHERE table_name='Data_SiteReport__Stages' AND column_name='sides'""")).Should().Be("_text");
    }

    [Fact]
    public async Task ParentAndBlocks_AreWrittenTogether_AndReadBackInOrder()
    {
        var b = await BuildAsync();
        var id = await _data.InsertWithSectionsAsync("Data_SiteReport", b.Version.Fields, Header(12),
            Rows(b, Block("walls", [b.FloorA, b.FloorB], null, true), Block("walls", null, ["1", "2"], false)), _user);

        var blocks = await _data.GetBlocksAsync("Data_SiteReport__Stages", b.Version.Fields.Where(f => f.SectionId != null).ToList(), [id]);
        blocks.Select(x => x.BlockIndex).Should().Equal(0, 1);
        ((Guid[])blocks[0].Values["floors"]!).Should().BeEquivalentTo(new[] { b.FloorA, b.FloorB });
        ((string[])blocks[1].Values["sides"]!).Should().Equal("1", "2");
        blocks[1].Values["plan_met"].Should().Be(false);
    }

    [Fact]
    public async Task AFailingBlock_LeavesNoParentRowBehind()
    {
        var b = await BuildAsync();
        // plan_met is NOT NULL in the child table: the second block fails inside the transaction.
        var act = () => _data.InsertWithSectionsAsync("Data_SiteReport", b.Version.Fields, Header(5),
            Rows(b, Block("walls", null, null, true), Block("walls", null, null, null)), _user);
        await act.Should().ThrowAsync<PostgresException>();
        (await Scalar<long>("""SELECT count(*) FROM "Data_SiteReport" """)).Should().Be(0);
        (await Scalar<long>("""SELECT count(*) FROM "Data_SiteReport__Stages" """)).Should().Be(0);
    }

    [Fact]
    public async Task Editing_ReplacesTheBlocks()
    {
        var b = await BuildAsync();
        var id = await _data.InsertWithSectionsAsync("Data_SiteReport", b.Version.Fields, Header(5),
            Rows(b, Block("walls", null, null, true), Block("walls", null, null, true)), _user);
        (await _data.UpdateWithSectionsAsync("Data_SiteReport", b.Version.Fields, id, Header(7),
            Rows(b, Block("walls", null, ["2"], false)), _user)).Should().BeTrue();

        var blocks = await _data.GetBlocksAsync("Data_SiteReport__Stages", b.Version.Fields.Where(f => f.SectionId != null).ToList(), [id]);
        blocks.Should().ContainSingle().Which.Values["plan_met"].Should().Be(false);
        (await Scalar<long>("""SELECT count(*) FROM "Data_SiteReport__Stages" WHERE "IsDeleted" """)).Should().Be(2, "the old blocks are kept, soft-deleted");
    }

    [Fact]
    public async Task TheSectionView_HasOneRowPerBlock_WithTheHeaderColumns_AndFloorNames()
    {
        var b = await BuildAsync();
        await _data.InsertWithSectionsAsync("Data_SiteReport", b.Version.Fields, Header(9),
            Rows(b, Block("walls", [b.FloorA, b.FloorB], null, true), Block("walls", [b.FloorB], null, false)), _user);
        await _data.InsertWithSectionsAsync("Data_SiteReport", b.Version.Fields, Header(4), Rows(b, Block("walls", null, null, true)), _user);

        (await Scalar<long>("""SELECT count(*) FROM "Report_SiteReport__Stages" """)).Should().Be(3);
        (await Scalar<long>("""SELECT count(*) FROM "Report_SiteReport__Stages" WHERE "العمالة" = 9 """)).Should().Be(2);
        (await Scalar<string>("""SELECT "الأدوار" FROM "Report_SiteReport__Stages" WHERE "Block" = 1 AND "العمالة" = 9""")).Should().Be("الدور 1, الدور 2");
        (await Scalar<string>("""SELECT "Submitted By" FROM "Report_SiteReport__Stages" LIMIT 1""")).Should().Be("Foreman");
        (await Scalar<long>("""SELECT count(*) FROM "Report_SiteReport" """)).Should().Be(2, "the main view stays one row per report");
    }

    [Fact]
    public async Task TheGuard_SerializesTwoWritesOnTheSameKey()
    {
        var b = await BuildAsync();
        var seen = new System.Collections.Concurrent.ConcurrentBag<long>();
        async Task Write() => await _data.InsertWithSectionsAsync("Data_SiteReport", b.Version.Fields, Header(1),
            Rows(b, Block("walls", null, null, true)), _user,
            new WriteGuard("project:1", async ct =>
            {
                seen.Add(await Scalar<long>("""SELECT count(*) FROM "Data_SiteReport" """));
                await Task.Delay(300, ct);
            }));

        await Task.WhenAll(Write(), Write());
        seen.Should().BeEquivalentTo(new long[] { 0, 1 }, "the second check runs only after the first write committed");
    }
}
