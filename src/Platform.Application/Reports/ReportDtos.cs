using Platform.Domain.Reports;

namespace Platform.Application.Reports;

public record ReportRecipientDto(
    Guid Id, string Name, string PhoneNumber, bool ReceivesDaily, bool ReceivesWeekly, bool ReceivesMonthly, bool IsActive)
{
    public static ReportRecipientDto From(ReportRecipient r) =>
        new(r.Id, r.Name, r.PhoneNumber, r.ReceivesDaily, r.ReceivesWeekly, r.ReceivesMonthly, r.IsActive);
}

public record ReportDeliveryDto(string RecipientName, string PhoneNumber, string Status, string? Error, DateTime? SentAtUtc);

public record ReportRunDto(
    Guid Id, string Kind, DateOnly PeriodStart, DateOnly PeriodEnd, string Trigger, string Status, string? Analysis,
    int InputTokens, int OutputTokens, decimal CostUsd, string? Error, DateTime CreatedAtUtc, DateTime? CompletedAtUtc,
    bool HasPdf, IReadOnlyList<ReportDeliveryDto> Deliveries);

/// <summary>Everything the Reports admin page shows.</summary>
public record ReportsOverviewDto(
    bool AnalysisConfigured, bool PdfConfigured, bool WhatsAppConfigured, string Model, string SendTime,
    decimal MonthlyBudgetUsd, decimal SpentThisMonthUsd,
    IReadOnlyList<ReportRecipientDto> Recipients, IReadOnlyList<ReportRunDto> Runs);
