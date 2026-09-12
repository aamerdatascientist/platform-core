using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Platform.Domain.Forms;
using Platform.Domain.Forms.Enums;
using Platform.Infrastructure.Persistence.DynamicSchema;
using Testcontainers.PostgreSql;
using Xunit;

namespace Platform.Infrastructure.IntegrationTests;

/// <summary>
/// These deliberately run against a real, throwaway Postgres container rather than a mock -
/// DynamicSchemaService's entire job is generating correct DDL/DML, and a mock would only
/// prove the test author's assumptions about Postgres, not Postgres's actual behavior.
/// Requires Docker to be running (not available in every environment - e.g. Claude Code's
/// own sandbox can't run these; see CLAUDE.md).
/// </summary>
public class DynamicSchemaServiceTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgresContainer = new PostgreSqlBuilder().Build();
    private DynamicSchemaService _sut = default!;
    private string _connectionString = default!;

    public async Task InitializeAsync()
    {
        await _postgresContainer.StartAsync();
        _connectionString = _postgresContainer.GetConnectionString();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:PostgresConnection"] = _connectionString
            })
            .Build();

        _sut = new DynamicSchemaService(configuration, NullLogger<DynamicSchemaService>.Instance);

        // RefreshReportingViewAsync joins to Users - a minimal stand-in table is enough
        // for these tests without pulling in the full ApplicationDbContext.
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """CREATE TABLE "Users" ("Id" uuid PRIMARY KEY, "DisplayName" varchar(200));""", connection);
        await command.ExecuteNonQueryAsync();
    }

    public async Task DisposeAsync() => await _postgresContainer.DisposeAsync();

    [Fact]
    public async Task EnsureTableForPublishedVersionAsync_CreatesTableWithColumnsForActiveFields()
    {
        var formDefinition = FormDefinition.Create("stock-adjustment", "Stock Adjustment", "StockManagement", null);
        var draft = formDefinition.GetDraftVersion();
        draft.AddField("quantity", "Quantity", FieldType.Number, true, null, null, null);
        draft.AddField("reason", "Reason", FieldType.ShortText, false, null, null, null);
        draft.MarkPublished();

        var tableName = await _sut.EnsureTableForPublishedVersionAsync(formDefinition, draft);

        tableName.Should().Be("Data_StockAdjustment");
        (await _sut.ColumnExistsAsync(tableName, "quantity")).Should().BeTrue();
        (await _sut.ColumnExistsAsync(tableName, "reason")).Should().BeTrue();
    }

    [Fact]
    public async Task EnsureTableForPublishedVersionAsync_IsAdditive_AcrossVersions()
    {
        var formDefinition = FormDefinition.Create("stock-count", "Stock Count", "StockManagement", null);
        var draftV1 = formDefinition.GetDraftVersion();
        draftV1.AddField("count", "Count", FieldType.Number, true, null, null, null);
        draftV1.MarkPublished();
        var tableName = await _sut.EnsureTableForPublishedVersionAsync(formDefinition, draftV1);
        formDefinition.MarkPublished(draftV1, tableName);

        var draftV2 = formDefinition.StartNewDraftVersion();
        draftV2.AddField("location", "Location", FieldType.ShortText, false, null, null, null);
        draftV2.MarkPublished();

        await _sut.EnsureTableForPublishedVersionAsync(formDefinition, draftV2);

        (await _sut.ColumnExistsAsync(tableName, "count")).Should().BeTrue("the original column must survive");
        (await _sut.ColumnExistsAsync(tableName, "location")).Should().BeTrue("the new column must be added");
    }

    [Fact]
    public async Task RefreshReportingViewAsync_ProducesAQueryableView()
    {
        var formDefinition = FormDefinition.Create("safety-check", "Safety Check", "Operations", null);
        var draft = formDefinition.GetDraftVersion();
        draft.AddField("passed", "Passed", FieldType.Boolean, true, null, null, null);
        draft.MarkPublished();
        var tableName = await _sut.EnsureTableForPublishedVersionAsync(formDefinition, draft);
        formDefinition.MarkPublished(draft, tableName);

        await _sut.RefreshReportingViewAsync(formDefinition, draft);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""SELECT COUNT(1) FROM "Report_SafetyCheck";""", connection);
        var act = async () => await command.ExecuteScalarAsync();

        await act.Should().NotThrowAsync("the generated view should be valid, queryable SQL");
    }

    /// <summary>
    /// The exact bug this session's diagnosis flagged: "lkp_" + a Lookup field's own Code can
    /// exceed Postgres's 63-byte identifier limit even when the Code alone is well under it.
    /// AddFieldDefinitionCommandValidator now blocks new Lookup fields from getting this long,
    /// but this exercises DynamicSchemaService's own runtime safety net directly (a field
    /// created before that validator existed, for instance), bypassing the command layer
    /// entirely via the domain API the same way the other tests here do.
    /// </summary>
    [Fact]
    public async Task RefreshReportingViewAsync_TruncatesAnOverlongLookupAlias_InsteadOfThrowing()
    {
        var targetForm = FormDefinition.Create("alias-target", "Alias Target", "StockManagement", null);
        var targetDraft = targetForm.GetDraftVersion();
        targetDraft.AddField("display_name", "Display Name", FieldType.ShortText, true, null, null, null);
        targetDraft.MarkPublished();
        var targetTableName = await _sut.EnsureTableForPublishedVersionAsync(targetForm, targetDraft);
        targetForm.MarkPublished(targetDraft, targetTableName);

        var overlongCode = "a" + new string('b', 60); // 61 chars - "lkp_" + this is 65, over the 63-byte limit
        var referencingForm = FormDefinition.Create("alias-referencing", "Alias Referencing", "StockManagement", null);
        var referencingDraft = referencingForm.GetDraftVersion();
        referencingDraft.AddField(overlongCode, "Target Reference", FieldType.Lookup, false, null, targetForm.Id, null);
        referencingDraft.MarkPublished();
        var referencingTableName = await _sut.EnsureTableForPublishedVersionAsync(referencingForm, referencingDraft);
        referencingForm.MarkPublished(referencingDraft, referencingTableName);

        var lookupTargets = new Dictionary<Guid, FormDefinition> { [targetForm.Id] = targetForm };

        var act = async () => await _sut.RefreshReportingViewAsync(referencingForm, referencingDraft, lookupTargets);

        await act.Should().NotThrowAsync("an over-length Lookup code must degrade to a truncated alias, not crash the publish");
    }

    /// <summary>
    /// Without the pg_advisory_xact_lock in EnsureTableForPublishedVersionAsync, two
    /// concurrent first-publish calls for the same form can both observe "table doesn't
    /// exist" via INFORMATION_SCHEMA before either commits its CREATE TABLE, and the loser
    /// fails with a raw duplicate-table error instead of the clean, idempotent outcome two
    /// concurrent publishes of the same form should have.
    /// </summary>
    [Fact]
    public async Task EnsureTableForPublishedVersionAsync_HandlesConcurrentFirstPublish_ForTheSameForm()
    {
        var formA = FormDefinition.Create("concurrent-form", "Concurrent Form", "StockManagement", null);
        var draftA = formA.GetDraftVersion();
        draftA.AddField("field_one", "Field One", FieldType.ShortText, false, null, null, null);
        draftA.MarkPublished();

        var formB = FormDefinition.Create("concurrent-form", "Concurrent Form", "StockManagement", null);
        var draftB = formB.GetDraftVersion();
        draftB.AddField("field_one", "Field One", FieldType.ShortText, false, null, null, null);
        draftB.MarkPublished();

        var taskA = _sut.EnsureTableForPublishedVersionAsync(formA, draftA);
        var taskB = _sut.EnsureTableForPublishedVersionAsync(formB, draftB);

        var act = async () => await Task.WhenAll(taskA, taskB);

        await act.Should().NotThrowAsync();
        (await _sut.ColumnExistsAsync("Data_ConcurrentForm", "field_one")).Should().BeTrue();
    }

    [Theory]
    [InlineData("bad; DROP TABLE \"Users\"; --")]
    [InlineData("has spaces")]
    [InlineData("")]
    public void AssertSafePostgresIdentifier_RejectsAnythingThatIsNotASafeIdentifier(string candidate)
    {
        var act = () => SqlTypeMapper.AssertSafePostgresIdentifier(candidate);
        act.Should().Throw<ArgumentException>();
    }
}
