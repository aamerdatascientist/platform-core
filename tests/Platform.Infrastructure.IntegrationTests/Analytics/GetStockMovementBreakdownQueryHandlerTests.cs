using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Platform.Application.Analytics;
using Platform.Application.Analytics.Queries.GetStockMovementBreakdown;
using Platform.Application.Common.Interfaces;
using Platform.Domain.Forms;
using Platform.Domain.Forms.Enums;
using Platform.Infrastructure.Persistence;
using Xunit;

namespace Platform.Infrastructure.IntegrationTests.Analytics;

public class GetStockMovementBreakdownQueryHandlerTests
{
    private static ApplicationDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static FormDefinition CreatePublishedForm(string code, string moduleName, string tableName)
    {
        var form = FormDefinition.Create(code, code, moduleName, null);
        var draft = form.GetDraftVersion();
        draft.AddField("placeholder", "Placeholder", FieldType.ShortText, false, null, null, null);
        draft.MarkPublished();
        form.MarkPublished(draft, tableName);
        return form;
    }

    /// <summary>
    /// The exact thing the Phase 0 audit flagged: each of the four movement forms names its
    /// quantity/location columns differently, and Material Issue's only real Lookup is a
    /// SOURCE location (from_location), not a destination - it's easy to wire the wrong
    /// column for the wrong table. This asserts on what was actually requested from the
    /// repository, not just on the DTOs that came back, so a swapped mapping fails here even
    /// if it would have "happened" to produce a plausible-looking result.
    /// </summary>
    [Fact]
    public async Task Handle_RequestsTheCorrectQuantityAndLocationColumn_PerMovementType()
    {
        await using var db = CreateContext();
        db.FormDefinitions.AddRange(
            CreatePublishedForm("goods-receipt", "StockManagement", "Data_GoodsReceipt"),
            CreatePublishedForm("material-issue", "StockManagement", "Data_MaterialIssue"),
            CreatePublishedForm("stock-transfer", "StockManagement", "Data_StockTransfer"),
            CreatePublishedForm("stock-adjustment", "StockManagement", "Data_StockAdjustment"));
        await db.SaveChangesAsync();

        var fakeDynamicData = new FakeDynamicDataRepository();
        var fakeExecutiveOverview = new FakeExecutiveOverviewRepository();

        var handler = new GetStockMovementBreakdownQueryHandler(db, fakeDynamicData, fakeExecutiveOverview);
        await handler.Handle(new GetStockMovementBreakdownQuery(), CancellationToken.None);

        fakeExecutiveOverview.MovementQuantityCalls.Should().BeEquivalentTo(new[]
        {
            ("Data_GoodsReceipt", "quantity_received", "location"),
            ("Data_MaterialIssue", "quantity_issued", "from_location"),
            ("Data_StockTransfer", "quantity", "to_location"),
            ("Data_StockAdjustment", "quantity_adjusted", "location"),
        });
    }

    [Fact]
    public async Task Handle_ResolvesMaterialAndLocationNames_AndPassesQuantitySignThrough()
    {
        await using var db = CreateContext();
        db.FormDefinitions.AddRange(
            CreatePublishedForm("materials", "StockManagement", "Data_Materials"),
            CreatePublishedForm("locations", "StockManagement", "Data_Locations"),
            CreatePublishedForm("goods-receipt", "StockManagement", "Data_GoodsReceipt"),
            CreatePublishedForm("stock-adjustment", "StockManagement", "Data_StockAdjustment"));
        await db.SaveChangesAsync();

        var steelId = Guid.NewGuid();
        var cementId = Guid.NewGuid();
        var warehouseId = Guid.NewGuid();
        var siteStoreId = Guid.NewGuid();

        var fakeDynamicData = new FakeDynamicDataRepository();
        fakeDynamicData.Rows["Data_Materials"] = new List<DynamicRow>
        {
            new(steelId, new Dictionary<string, object?> { ["item_code"] = "MAT-001", ["description"] = "Rebar 16mm" }),
            new(cementId, new Dictionary<string, object?> { ["item_code"] = "MAT-002", ["description"] = "Cement 50kg" }),
        };
        fakeDynamicData.Rows["Data_Locations"] = new List<DynamicRow>
        {
            new(warehouseId, new Dictionary<string, object?> { ["location_name"] = "Central Warehouse" }),
            new(siteStoreId, new Dictionary<string, object?> { ["location_name"] = "Al Waha Site Store" }),
        };

        var fakeExecutiveOverview = new FakeExecutiveOverviewRepository();
        fakeExecutiveOverview.MovementQuantities["Data_GoodsReceipt"] = new List<MovementQuantityRow>
        {
            new(steelId, warehouseId, 500m),
            new(cementId, siteStoreId, 120m),
        };
        // A negative adjustment total - the handler must pass this through exactly as
        // returned, not assume a sign convention (found vs. shortage) that hasn't been
        // checked against real data.
        fakeExecutiveOverview.MovementQuantities["Data_StockAdjustment"] = new List<MovementQuantityRow>
        {
            new(steelId, warehouseId, -12.5m),
        };

        var handler = new GetStockMovementBreakdownQueryHandler(db, fakeDynamicData, fakeExecutiveOverview);
        var result = await handler.Handle(new GetStockMovementBreakdownQuery(), CancellationToken.None);

        result.Should().HaveCount(3);

        var steelReceipt = result.Single(r => r.MovementType == StockMovementType.GoodsReceipt && r.MaterialId == steelId);
        steelReceipt.MaterialCode.Should().Be("MAT-001");
        steelReceipt.LocationName.Should().Be("Central Warehouse");
        steelReceipt.TotalQuantity.Should().Be(500m);

        var cementReceipt = result.Single(r => r.MovementType == StockMovementType.GoodsReceipt && r.MaterialId == cementId);
        cementReceipt.LocationName.Should().Be("Al Waha Site Store");

        var adjustment = result.Single(r => r.MovementType == StockMovementType.StockAdjustment);
        adjustment.TotalQuantity.Should().Be(-12.5m, "sign convention is unconfirmed - the raw signed sum must pass through unmodified");
    }

    [Fact]
    public async Task Handle_SkipsMovementTypesWhoseFormDoesNotExist_WithoutThrowing()
    {
        await using var db = CreateContext();
        // No forms published at all - every movement type, and both master-data forms, are missing.
        var handler = new GetStockMovementBreakdownQueryHandler(
            db, new FakeDynamicDataRepository(), new FakeExecutiveOverviewRepository());

        var result = await handler.Handle(new GetStockMovementBreakdownQuery(), CancellationToken.None);

        result.Should().BeEmpty();
    }
}
