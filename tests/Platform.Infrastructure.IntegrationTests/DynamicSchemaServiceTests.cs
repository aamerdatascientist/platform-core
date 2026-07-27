using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Platform.Domain.Forms;
using Platform.Domain.Forms.Enums;
using Platform.Infrastructure.Persistence.DynamicSchema;
using Testcontainers.MsSql;
using Xunit;

namespace Platform.Infrastructure.IntegrationTests;

/// <summary>
/// These deliberately run against a real, throwaway SQL Server container rather than a
/// mock - DynamicSchemaService's entire job is generating correct T-SQL, and a mock
/// would only prove the test author's assumptions about SQL Server, not SQL Server's
/// actual behavior. Requires Docker to be running.
/// </summary>
public class DynamicSchemaServiceTests : IAsyncLifetime
{
    private readonly MsSqlContainer _sqlContainer = new MsSqlBuilder().Build();
    private DynamicSchemaService _sut = default!;
    private string _connectionString = default!;

    public async Task InitializeAsync()
    {
        await _sqlContainer.StartAsync();
        _connectionString = _sqlContainer.GetConnectionString();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = _connectionString
            })
            .Build();

        _sut = new DynamicSchemaService(configuration, NullLogger<DynamicSchemaService>.Instance);

        // RefreshReportingViewAsync joins to Users - a minimal stand-in table is enough
        // for these tests without pulling in the full ApplicationDbContext.
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(
            "CREATE TABLE [Users] ([Id] uniqueidentifier PRIMARY KEY, [DisplayName] nvarchar(200));", connection);
        await command.ExecuteNonQueryAsync();
    }

    public async Task DisposeAsync() => await _sqlContainer.DisposeAsync();

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

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("SELECT COUNT(1) FROM [Report_SafetyCheck];", connection);
        var act = async () => await command.ExecuteScalarAsync();

        await act.Should().NotThrowAsync("the generated view should be valid, queryable SQL");
    }

    [Theory]
    [InlineData("bad; DROP TABLE Users; --")]
    [InlineData("has spaces")]
    [InlineData("")]
    public void AssertSafeIdentifier_RejectsAnythingThatIsNotASafeIdentifier(string candidate)
    {
        var act = () => SqlTypeMapper.AssertSafeIdentifier(candidate);
        act.Should().Throw<ArgumentException>();
    }
}
