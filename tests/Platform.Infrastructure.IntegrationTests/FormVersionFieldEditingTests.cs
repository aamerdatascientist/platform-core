using FluentAssertions;
using Platform.Domain.Forms;
using Platform.Domain.Forms.Enums;
using Xunit;

namespace Platform.Infrastructure.IntegrationTests;

/// <summary>
/// Pure domain behaviour for the field-editing operations - no database, no container, so
/// these run anywhere (unlike DynamicSchemaServiceTests, which needs Docker).
/// </summary>
public class FormVersionFieldEditingTests
{
    private static FormVersion DraftWithFields(params string[] codes)
    {
        var form = FormDefinition.Create("editing", "Editing", "Operations", null);
        var draft = form.GetDraftVersion();
        foreach (var code in codes)
            draft.AddField(code, code, FieldType.ShortText, false, null, null, null);
        return draft;
    }

    [Fact]
    public void AddField_AssignsSequentialDisplayOrder()
    {
        var draft = DraftWithFields("a", "b", "c");

        draft.Fields.Select(f => f.DisplayOrder).Should().Equal(0, 1, 2);
    }

    /// <summary>
    /// The exact side effect the old remove-and-re-add edit mechanism caused on Form 4: after
    /// removing a field from the middle, DisplayOrder was assigned as _fields.Count, which
    /// collides with whatever field already held that order - so the re-added field landed on
    /// top of another one instead of at the end.
    /// </summary>
    [Fact]
    public void AddField_AfterARemoval_DoesNotReuseAnExistingDisplayOrder()
    {
        var draft = DraftWithFields("a", "b", "c", "d");
        var middle = draft.Fields.Single(f => f.Code == "b");

        draft.RemoveField(middle.Id);
        draft.AddField("e", "e", FieldType.ShortText, false, null, null, null);

        var orders = draft.Fields.Select(f => f.DisplayOrder).ToList();
        orders.Should().OnlyHaveUniqueItems("a re-added field must not collide with an existing field's order");
        draft.Fields.Single(f => f.Code == "e").DisplayOrder.Should().Be(4);
    }

    [Fact]
    public void ReorderFields_AppliesTheGivenOrder()
    {
        var draft = DraftWithFields("a", "b", "c");
        var byCode = draft.Fields.ToDictionary(f => f.Code, f => f.Id);

        draft.ReorderFields(new[] { byCode["c"], byCode["a"], byCode["b"] });

        draft.Fields.OrderBy(f => f.DisplayOrder).Select(f => f.Code).Should().Equal("c", "a", "b");
    }

    [Fact]
    public void ReorderFields_RejectsAPartialList()
    {
        var draft = DraftWithFields("a", "b", "c");
        var first = draft.Fields.First().Id;

        var act = () => draft.ReorderFields(new[] { first });

        act.Should().Throw<InvalidOperationException>(
            "a partial list would silently leave the omitted fields where they were");
    }

    [Fact]
    public void ReorderFields_IgnoresArchivedFields()
    {
        var draft = DraftWithFields("a", "b", "c");
        var archived = draft.Fields.Single(f => f.Code == "b");
        draft.DeactivateField(archived.Id);

        var activeIds = draft.Fields.Where(f => f.IsActive).Select(f => f.Id).Reverse().ToList();
        var act = () => draft.ReorderFields(activeIds);

        act.Should().NotThrow("archived fields aren't displayed, so they aren't part of the order");
        draft.Fields.Single(f => f.Code == "c").DisplayOrder.Should().Be(0);
    }

    [Fact]
    public void AddField_OnAPublishedVersion_IsAllowed()
    {
        var draft = DraftWithFields("a");
        draft.MarkPublished();

        var act = () => draft.AddField("b", "b", FieldType.ShortText, false, null, null, null);

        act.Should().NotThrow("a published form is edited live now - the draft-only guard is gone");
    }

    [Fact]
    public void RenameCode_NormalisesAndRejectsAnythingThatIsNotASqlIdentifier()
    {
        var field = DraftWithFields("a").Fields.Single();

        field.RenameCode("Quantity Received");
        field.Code.Should().Be("quantity_received");

        var act = () => field.RenameCode("كمية");
        act.Should().Throw<ArgumentException>("Code becomes a physical column name and is interpolated into DDL");
    }

    [Fact]
    public void ChangeType_DropsCompanionDataThatNoLongerApplies()
    {
        var form = FormDefinition.Create("companion", "Companion", "Operations", null);
        var draft = form.GetDraftVersion();
        var field = draft.AddField(
            "choice", "Choice", FieldType.Dropdown, false, "[{\"value\":\"a\",\"label\":\"A\"}]", null, null);

        field.ChangeType(FieldType.Number, null, null);

        field.FieldType.Should().Be(FieldType.Number);
        field.OptionsJson.Should().BeNull("a Number field carrying a stale options list describes a shape it no longer has");
    }

    [Fact]
    public void ChangeType_StillRequiresCompanionDataForTypesThatNeedIt()
    {
        var field = DraftWithFields("a").Fields.Single();

        var toDropdownWithNoOptions = () => field.ChangeType(FieldType.Dropdown, null, null);
        toDropdownWithNoOptions.Should().Throw<ArgumentException>();

        var toLookupWithNoTarget = () => field.ChangeType(FieldType.Lookup, null, null);
        toLookupWithNoTarget.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void DeactivateAndReactivate_RoundTripWithoutTouchingAnythingElse()
    {
        var field = DraftWithFields("a").Fields.Single();
        var originalCode = field.Code;

        field.Deactivate();
        field.IsActive.Should().BeFalse();

        field.Reactivate();
        field.IsActive.Should().BeTrue();
        field.Code.Should().Be(originalCode, "archiving is a visibility change, not a data change");
    }
}
