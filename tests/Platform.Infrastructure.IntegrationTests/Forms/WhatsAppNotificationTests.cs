using FluentAssertions;
using Platform.Application.Notifications;
using Platform.Infrastructure.Notifications;
using Xunit;

namespace Platform.Infrastructure.IntegrationTests.Forms;

/// <summary>Phone number clean-up and the reminder schedule - the parts of WhatsApp that need no network.</summary>
public class WhatsAppNotificationTests
{
    [Theory]
    [InlineData("0551234567", "966551234567")]
    [InlineData("+966 55 123 4567", "966551234567")]
    [InlineData("00966551234567", "966551234567")]
    [InlineData("966-55-123-4567", "966551234567")]
    [InlineData("+44 7700 900123", "447700900123")]
    public void PhoneNumbers_BecomeInternationalDigits(string input, string expected) =>
        PhoneNumbers.Normalize(input).Should().Be(expected);

    [Fact]
    public void PhoneNumbers_BlankClears_AndNonsenseIsRefused()
    {
        PhoneNumbers.Normalize("  ").Should().BeNull();
        FluentActions.Invoking(() => PhoneNumbers.Normalize("12345")).Should().Throw<FormatException>();
        FluentActions.Invoking(() => PhoneNumbers.Normalize("055-ABC-4567")).Should().Throw<FormatException>();
    }

    [Theory]
    [InlineData("08:59", null)]
    [InlineData("09:00", 1)]
    [InlineData("10:59", 1)]
    [InlineData("11:00", null)] // more than 2 hours late: skipped, not sent at an odd hour
    [InlineData("14:30", 2)]
    public void TheLatestPassedReminderTime_IsDue(string now, int? slot) =>
        AssignmentReminderService.DueSlot(new[] { "09:00", "14:00" }, TimeOnly.Parse(now)).Should().Be(slot);

    [Fact]
    public void Dates_InMessages_AreGregorianInArabic() =>
        AssignmentNotifier.ArabicDate(new DateOnly(2026, 11, 3)).Should().Be("3 نوفمبر 2026");
}
