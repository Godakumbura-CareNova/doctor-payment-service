namespace DoctorPayment.Api.DTOs.Dashboard;

public sealed record AdminMedicinePaymentDashboardDto(int TotalPayments, IReadOnlyDictionary<string, int> StatusCounts,
    PaymentDashboardQueue PendingTransfers, IReadOnlyList<PaymentDashboardRow> RecentPayments, DashboardIncome Income, DateTime GeneratedAtUtc);
