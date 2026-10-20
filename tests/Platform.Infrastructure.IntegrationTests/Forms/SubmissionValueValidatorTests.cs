using System.Text.Json;
using FluentAssertions;
using Platform.Application.Forms;
using Platform.Domain.Forms;
using Platform.Domain.Forms.Enums;
using Xunit;

namespace Platform.Infrastructure.IntegrationTests.Forms;

/// <summary>
/// The server-side checks every submission and every record edit goes through. Values are
/// built as JsonElement, the shape the API's model binder actually hands over.
/// </summary>
public class SubmissionValueValidatorTests
{
    private static readonly DateOnly SaudiToday = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3));

    private static FormVersion NewVersion() => FormDefinition.Create("t", "T", "Ops", null).GetDraftVersion();

    private static Dictionary<string, object?> Values(object anonymous) =>
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(JsonSerializer.Serialize(anonymous))!
            .ToDictionary(kv => kv.Key, kv => (object?)kv.Value);

    [Fact]
    public void WhitespaceOnlyText_DoesNotCountAsAnAnswer()
    {
        var v = NewVersion();
        v.AddField("notes", "Notes", FieldType.LongText, true, null, null, null);

        var errors = SubmissionValueValidator.Validate(v.Fields, Values(new { notes = "   " }));

        errors.Should().ContainKey("notes");
    }

    [Fact]
    public void Normalize_TrimsText_AndTurnsBlankIntoNull()
    {
        var v = NewVersion();
        v.AddField("notes", "Notes", FieldType.LongText, false, null, null, null);
        v.AddField("name", "Name", FieldType.ShortText, false, null, null, null);
        v.AddField("crew", "Crew", FieldType.Number, false, null, null, null);

        var normalized = SubmissionValueValidator.Normalize(v.Fields, Values(new { notes = "  ", name = "  Tower A ", crew = 5 }));

        normalized["notes"].Should().BeNull();
        normalized["name"].Should().Be("Tower A");
        normalized["crew"].Should().BeOfType<JsonElement>("non-text values pass through untouched");
    }

    [Fact]
    public void NoFutureDates_RejectsTomorrow_ButAcceptsToday()
    {
        var v = NewVersion();
        v.AddField("report_date", "Date", FieldType.DateTime, true, null, null, """{"noFutureDates":true}""");

        SubmissionValueValidator.Validate(v.Fields, Values(new { report_date = SaudiToday.ToString("yyyy-MM-dd") }))
            .Should().BeEmpty();
        SubmissionValueValidator.Validate(v.Fields, Values(new { report_date = SaudiToday.AddDays(1).ToString("yyyy-MM-dd") }))
            .Should().ContainKey("report_date");
    }

    [Fact]
    public void AnAbsurdYear_IsNotAValidDate()
    {
        var v = NewVersion();
        v.AddField("d", "Date", FieldType.DateTime, false, null, null, null);

        SubmissionValueValidator.Validate(v.Fields, Values(new { d = "0001-01-01" })).Should().ContainKey("d");
    }

    [Fact]
    public void MinAndMinExclusive_AreEnforced()
    {
        var v = NewVersion();
        v.AddField("crew", "Crew", FieldType.Number, true, null, null, """{"min":0}""");
        v.AddField("qty", "Qty", FieldType.Decimal, true, null, null, """{"min":0,"minExclusive":true}""");

        SubmissionValueValidator.Validate(v.Fields, Values(new { crew = 0, qty = 0.5m })).Should().BeEmpty();

        var errors = SubmissionValueValidator.Validate(v.Fields, Values(new { crew = -5, qty = 0 }));
        errors.Should().ContainKey("crew");
        errors["qty"].Single().Should().Contain("more than 0");
    }

    [Fact]
    public void ValuesTheDatabaseCannotHold_AreRejectedCleanly()
    {
        var v = NewVersion();
        v.AddField("crew", "Crew", FieldType.Number, false, null, null, null);
        v.AddField("qty", "Qty", FieldType.Decimal, false, null, null, null);
        v.AddField("name", "Name", FieldType.ShortText, false, null, null, null);

        var errors = SubmissionValueValidator.Validate(
            v.Fields, Values(new { crew = 5_000_000_000L, qty = 1e15m, name = new string('x', 401) }));

        errors.Keys.Should().BeEquivalentTo("crew", "qty", "name");
    }

    // The live Daily Progress Report stores some conditions as a single JSON string rather
    // than an array, and is driven by a yes/no field - both used to make the dependent field
    // never required.
    [Theory]
    [InlineData("\"true\"")]
    [InlineData("[\"true\"]")]
    [InlineData("[true]")]
    [InlineData("\"[\\\"true\\\"]\"")]
    public void ConditionallyRequiredField_IsRequired_WhenABooleanControllerMatches(string visibleWhenValuesJson)
    {
        var v = NewVersion();
        v.AddField("had_delay", "Delay?", FieldType.Boolean, true, null, null, null);
        v.AddField("delay_cause", "Cause", FieldType.LongText, true, null, null, null, "had_delay", visibleWhenValuesJson);

        SubmissionValueValidator.Validate(v.Fields, Values(new { had_delay = true })).Should().ContainKey("delay_cause");
        SubmissionValueValidator.Validate(v.Fields, Values(new { had_delay = false })).Should().BeEmpty();
    }

    [Fact]
    public void ASingleStringCondition_MatchesExactly_NotAsASubstring()
    {
        var v = NewVersion();
        v.AddField("phase", "Phase", FieldType.ShortText, true, null, null, null);
        v.AddField("zone", "Zone", FieldType.ShortText, true, null, null, null, "phase", "\"excavation\"");

        SubmissionValueValidator.Validate(v.Fields, Values(new { phase = "excavation" })).Should().ContainKey("zone");
        SubmissionValueValidator.Validate(v.Fields, Values(new { phase = "excav" })).Should().BeEmpty();
    }

    [Fact]
    public void ParseAllowedValues_ReadsEveryStoredShape()
    {
        SubmissionValueValidator.ParseAllowedValues("""["a","b"]""").Should().Equal("a", "b");
        SubmissionValueValidator.ParseAllowedValues("\"a\"").Should().Equal("a");
        SubmissionValueValidator.ParseAllowedValues("true").Should().Equal("true");
        SubmissionValueValidator.ParseAllowedValues("not json").Should().BeEmpty();
        SubmissionValueValidator.ParseAllowedValues(null).Should().BeEmpty();
    }
}
