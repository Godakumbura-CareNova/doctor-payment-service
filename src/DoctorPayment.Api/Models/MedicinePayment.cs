using System.ComponentModel.DataAnnotations;

namespace DoctorPayment.Api.Models;

public enum PaymentMethod
{
    Online,
    BankTransfer,
    Cash
}

public enum MedicinePaymentStatus
{
    /// <summary>Online chosen; the patient still has to complete the gateway step.</summary>
    AwaitingOnlinePayment,

    /// <summary>Bank transfer chosen; the patient transfers the amount and submits the receipt.</summary>
    AwaitingBankTransfer,

    /// <summary>Cash chosen; paid at the pharmacy counter when the order is collected. No online payment is started.</summary>
    PayOnCollection,

    /// <summary>A bank transfer receipt was uploaded and waits for staff. The order stays Pending and is not confirmed.</summary>
    ReceiptUnderVerification,

    /// <summary>Staff could not match the uploaded receipt; the patient can upload another.</summary>
    ReceiptRejected,

    /// <summary>The payment is confirmed: a verified receipt, or staff recorded the online or cash payment.</summary>
    Paid,

    /// <summary>Staff recorded that the payment did not go through (e.g. online payment declined, cash never paid).</summary>
    Failed
}

/// <summary>
/// How a patient pays for one pharmacy order. The order itself lives in the pharmacy-order service; this row keeps
/// a snapshot of its number and total from when the method was chosen. One row per order: choosing again replaces
/// the method while nothing has been paid.
/// </summary>
public class MedicinePayment
{
    public int Id { get; set; }

    /// <summary>The pharmacy-order service's order id.</summary>
    public int OrderId { get; set; }

    public Guid PatientId { get; set; }

    [MaxLength(50)]
    public string OrderNumber { get; set; } = string.Empty;

    public decimal Amount { get; set; }

    public PaymentMethod Method { get; set; }

    public MedicinePaymentStatus Status { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    /// <summary>
    /// The pharmacy order's status as last seen here ("Pending", "ReadyForCollection", "Collected" or "Rejected"). The
    /// pharmacy-order service owns the order; this copy lets admins see it and is refreshed when staff open or update it.
    /// </summary>
    [MaxLength(30)]
    public string OrderStatus { get; set; } = MedicineOrderStatuses.Pending;

    /// <summary>Order status changes made through payment management, oldest first.</summary>
    public List<MedicineOrderStatusChange> OrderStatusHistory { get; set; } = new();

    /// <summary>The patient's name from their token when the method was chosen; empty for payments made before it was kept.</summary>
    [MaxLength(200)]
    public string CustomerName { get; set; } = string.Empty;

    /// <summary>Bank transfer receipts uploaded for this payment, oldest first.</summary>
    public List<BankTransferReceipt> Receipts { get; set; } = new();

    /// <summary>
    /// The order's medicines when the method was chosen, so staff and admins can see them without the pharmacy-order
    /// service (which only lets the patient and pharmacy staff read orders).
    /// </summary>
    public List<MedicinePaymentItem> Items { get; set; } = new();

    /// <summary>Every status change, oldest first, for monitoring payment activity.</summary>
    public List<MedicinePaymentStatusChange> StatusHistory { get; set; } = new();
}
