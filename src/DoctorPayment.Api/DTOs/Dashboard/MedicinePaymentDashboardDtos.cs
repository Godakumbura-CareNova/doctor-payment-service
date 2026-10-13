namespace DoctorPayment.Api.DTOs.Dashboard;

public record PaymentDashboardRow(int Id, int OrderId, string Reference, decimal Amount, string Method,
    string Status, DateTime? ConfirmedAtUtc, DateTime UpdatedAtUtc);
public record PaymentDashboardQueue(int TotalCount, IReadOnlyList<PaymentDashboardRow> Items);
public record DashboardIncome(decimal TodayAmount, decimal MonthAmount, int UndatedSuccessfulCount,
    decimal UndatedSuccessfulAmount, string Currency, string TimeZone, DateTime AsOfUtc);
public record PatientMedicinePaymentDashboard(IReadOnlyDictionary<string, int> StatusCounts,
    IReadOnlyList<PaymentDashboardRow> RecentPayments, DateTime GeneratedAtUtc);
public record StaffMedicinePaymentDashboard(PaymentDashboardQueue PendingTransfers,
    IReadOnlyList<PaymentDashboardRow> RecentPayments, DashboardIncome Income, DateTime GeneratedAtUtc);
