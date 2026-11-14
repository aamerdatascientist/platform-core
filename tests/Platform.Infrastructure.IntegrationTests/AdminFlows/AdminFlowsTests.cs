using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Platform.Api.Controllers;
using Platform.Application.Common.Interfaces;
using Platform.Application.StageProgress;
using Platform.Domain.Forms;
using Platform.Domain.Forms.Enums;
using Platform.Infrastructure.IntegrationTests.Analytics;
using Platform.Infrastructure.Persistence;
using Xunit;

namespace Platform.Infrastructure.IntegrationTests.AdminFlows;

/// <summary>Admin flows 2, 3, 5 (spec ADMIN-FLOWS-2-3-5, 2026-11-14): server side.</summary>
public class AdminFlowsTests
{
    private static bool AdminOnly(MemberInfo member) =>
        member.GetCustomAttributes<AuthorizeAttribute>(true).Any(a => a.Roles?.Split(',').Contains("Administrator") == true);

    [Fact]
    public void TheStageProgressChooserEndpoint_IsAdministratorsOnly()
    {
        var controller = typeof(StageProgressController);
        AdminOnly(controller).Should().BeTrue("the whole controller is administrator-only");
        var list = controller.GetMethod(nameof(StageProgressController.ListProjects))!;
        list.GetCustomAttribute<HttpGetAttribute>()!.Template.Should().Be("projects");
        list.GetCustomAttributes<AllowAnonymousAttribute>().Should().BeEmpty();
    }

    [Theory]
    [InlineData(nameof(FormAssignmentsController.Create))]
    [InlineData(nameof(FormAssignmentsController.List))]
    public void AssigningForms_StaysAdministratorsOnly(string action) =>
        AdminOnly(typeof(FormAssignmentsController).GetMethod(action)!).Should().BeTrue();

    [Fact]
    public void ChangingUsers_StaysAdministratorsOnly() =>
        AdminOnly(typeof(UsersController)).Should().BeTrue();

    [Fact]
    public async Task TheChooser_ListsEveryProject_ByCode()
    {
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var repo = new FakeDynamicDataRepository();
        foreach (var (code, table) in new[] { (StageForms.Projects, "Data_Projects"), (StageForms.Floors, "Data_ProjectFloors") })
        {
            var form = FormDefinition.Create(code, code, "Operations", null);
            var v = form.GetDraftVersion();
            v.AddField("project_code", "Code", FieldType.ShortText, false, null, null, null);
            v.MarkPublished();
            form.MarkPublished(v, table);
            db.FormDefinitions.Add(form);
        }
        await db.SaveChangesAsync();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        repo.Rows["Data_Projects"] = new List<DynamicRow>
        {
            new(b, new Dictionary<string, object?> { ["project_code"] = "120", ["project_name"] = "نزل ٢", ["status"] = "planning" }),
            new(a, new Dictionary<string, object?> { ["project_code"] = "119", ["project_name"] = "نزل", ["status"] = "active" }),
        };
        repo.Rows["Data_ProjectFloors"] = new List<DynamicRow>();

        var result = await new ListStageProjectsQueryHandler(new StageProgressData(db, repo)).Handle(new ListStageProjectsQuery(), default);

        result.Select(p => p.Code).Should().Equal("119", "120");
        result[0].Should().Be(new StageProjectSummaryDto(a, "119", "نزل", "active"));
    }
}
