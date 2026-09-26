using System.ComponentModel.DataAnnotations;

namespace DoctorPayment.Api.Models;

public enum BankTransferReceiptStatus
{
    /// <summary>Uploaded and waiting for pharmacy staff. The order stays Pending and is not confirmed.</summary>
    PendingVerification,

    /// <summary>Staff matched the transfer; the payment is Paid.</summary>
    Verified,

    /// <summary>Staff could not match the transfer; the patient can upload another receipt.</summary>
    Rejected
}

/// <summary>
/// A receipt a patient uploaded as proof of a bank transfer for one medicine order. Every upload is kept, so a
/// rejected receipt and its replacement can both be traced.
/// </summary>
public class BankTransferReceipt
{
    public int Id { get; set; }

    public int MedicinePaymentId { get; set; }

    public MedicinePayment? MedicinePayment { get; set; }

    /// <summary>The pharmacy-order service's order id, copied from the payment for staff lookups.</summary>
    public int OrderId { get; set; }

    public Guid PatientId { get; set; }

    /// <summary>The patient's name from their token when they uploaded, for the verification event and staff list.</summary>
    [MaxLength(200)]
    public string CustomerName { get; set; } = string.Empty;

    /// <summary>The name the patient's file had; shown only, never used as a path.</summary>
    [MaxLength(255)]
    public string OriginalFileName { get; set; } = string.Empty;

    /// <summary>The generated name the file is stored under.</summary>
    [MaxLength(100)]
    public string StoredFileName { get; set; } = string.Empty;

    [MaxLength(50)]
    public string ContentType { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    public BankTransferReceiptStatus Status { get; set; }

    /// <summary>
    /// The payment id while this receipt is waiting for verification, otherwise null. A unique index on it lets the
    /// database refuse a second waiting receipt for the same payment, even when two uploads race.
    /// </summary>
    public int? PendingPaymentId { get; set; }

    /// <summary>The Kafka event that announced this upload, to trace it through the outbox.</summary>
    public Guid UploadEventId { get; set; }

    public DateTime UploadedAtUtc { get; set; }

    public Guid? ReviewedBy { get; set; }

    public DateTime? ReviewedAtUtc { get; set; }

    [MaxLength(500)]
    public string? RejectionReason { get; set; }
}
