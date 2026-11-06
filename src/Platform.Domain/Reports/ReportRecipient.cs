using Platform.Domain.Common;

namespace Platform.Domain.Reports;

/// <summary>
/// Someone outside the app (a project stakeholder) who receives the scheduled PDF reports on
/// WhatsApp. Not a User: stakeholders don't sign in. Each report kind can be switched off per
/// recipient.
/// </summary>
public class ReportRecipient : AuditableEntity
{
    public string Name { get; private set; } = default!;

    /// <summary>International digits without "+", e.g. 9665XXXXXXXX (see PhoneNumbers.Normalize).</summary>
    public string PhoneNumber { get; private set; } = default!;

    public bool ReceivesDaily { get; private set; }
    public bool ReceivesWeekly { get; private set; }
    public bool ReceivesMonthly { get; private set; }
    public bool IsActive { get; private set; }

    private ReportRecipient() { }

    public static ReportRecipient Create(string name, string phoneNumber, bool daily, bool weekly, bool monthly) => new()
    {
        Name = name.Trim(),
        PhoneNumber = phoneNumber,
        ReceivesDaily = daily,
        ReceivesWeekly = weekly,
        ReceivesMonthly = monthly,
        IsActive = true,
    };

    public void Update(string name, string phoneNumber, bool daily, bool weekly, bool monthly, bool isActive)
    {
        Name = name.Trim();
        PhoneNumber = phoneNumber;
        ReceivesDaily = daily;
        ReceivesWeekly = weekly;
        ReceivesMonthly = monthly;
        IsActive = isActive;
    }

    public bool Receives(string kind) => IsActive && kind switch
    {
        ReportRun.Daily => ReceivesDaily,
        ReportRun.Weekly => ReceivesWeekly,
        ReportRun.Monthly => ReceivesMonthly,
        _ => false,
    };
}
