using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Platform.Application.Analytics.Queries.GetSubmissionCountByModule;
using Platform.Domain.Forms;
using Platform.Infrastructure.Persistence;
using Xunit;

namespace Platform.Infrastructure.IntegrationTests.Analytics;

public class GetSubmissionCountByModuleQueryHandlerTests
{
    private static ApplicationDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static FormDefinition CreatePublishedForm(string code, string name, string moduleName, string tableName)
    {
        var form = FormDefinition.Create(code, name, moduleName, null);
        form.MarkPublished(form.GetDraftVersion(), tableName);
        return form;
    }

    [Fact]
    public async Task Handle_SumsAcrossMultipleFormsInTheSameModule_AndGroupsByModule()
    {
        await using var db = CreateContext();
        var fakeRepository = new FakeDynamicDataRepository();

        // StockManagement: three published forms.
        var materials = CreatePublishedForm("materials", "Materials", "StockManagement", "Data_Materials");
        var locations = CreatePublishedForm("locations", "Locations", "StockManagement", "Data_Locations");
        var goodsReceipt = CreatePublishedForm("goods-receipt", "Goods Receipt", "StockManagement", "Data_GoodsReceipt");
        fakeRepository.Counts["Data_Materials"] = 14;
        fakeRepository.Counts["Data_Locations"] = 3;
        fakeRepository.Counts["Data_GoodsReceipt"] = 8;

        // Operations: two published forms.
        var projects = CreatePublishedForm("projects", "Projects", "Operations", "Data_Projects");
        var equipmentLog = CreatePublishedForm("equipment-log", "Equipment Log", "Operations", "Data_EquipmentLog");
        fakeRepository.Counts["Data_Projects"] = 3;
        fakeRepository.Counts["Data_EquipmentLog"] = 21;

        // A draft form with no TableName - never published, so it has no Data_* table to
        // count rows in, and must not appear in the results at all.
        var draftForm = FormDefinition.Create("draft-form", "Draft Form", "StockManagement", null);

        db.FormDefinitions.AddRange(materials, locations, goodsReceipt, projects, equipmentLog, draftForm);
        await db.SaveChangesAsync();

        var handler = new GetSubmissionCountByModuleQueryHandler(db, fakeRepository);
        var result = await handler.Handle(new GetSubmissionCountByModuleQuery(), CancellationToken.None);

        result.Should().HaveCount(2);
        result.Select(m => m.ModuleName).Should().ContainInOrder("Operations", "StockManagement");

        var stockManagement = result.Single(m => m.ModuleName == "StockManagement");
        stockManagement.Forms.Should().HaveCount(3);
        stockManagement.Forms.Sum(f => f.SubmissionCount).Should().Be(25, "14 + 3 + 8 across the three Stock Management forms");
        stockManagement.Forms.Select(f => f.FormCode).Should().ContainInOrder("materials", "goods-receipt", "locations");

        var operations = result.Single(m => m.ModuleName == "Operations");
        operations.Forms.Should().HaveCount(2);
        operations.Forms.Sum(f => f.SubmissionCount).Should().Be(24, "21 + 3 across the two Operations forms");
        operations.Forms.Select(f => f.FormCode).Should().ContainInOrder("equipment-log", "projects");

        result.SelectMany(m => m.Forms).Should().NotContain(f => f.FormCode == "draft-form",
            "an unpublished form has no Data_* table to count rows in");
    }

    [Fact]
    public async Task Handle_ReturnsEmpty_WhenNoFormsArePublished()
    {
        await using var db = CreateContext();
        db.FormDefinitions.Add(FormDefinition.Create("draft-only", "Draft Only", "Operations", null));
        await db.SaveChangesAsync();

        var handler = new GetSubmissionCountByModuleQueryHandler(db, new FakeDynamicDataRepository());
        var result = await handler.Handle(new GetSubmissionCountByModuleQuery(), CancellationToken.None);

        result.Should().BeEmpty();
    }
}
