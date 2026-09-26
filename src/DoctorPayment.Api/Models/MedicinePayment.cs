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

    /// <summary>Staff verified the bank transfer receipt; the payment is confirmed.</summary>
    Paid
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

    /// <summary>Bank transfer receipts uploaded for this payment, oldest first.</summary>
    public List<BankTransferReceipt> Receipts { get; set; } = new();
}
