namespace DoctorPayment.Api.DTOs.MedicinePayment;

/// <summary>An uploaded bank transfer receipt as the patient sees it.</summary>
public class BankTransferReceiptDto
{
    public int Id { get; set; }

    public string FileName { get; set; } = string.Empty;

    public string ContentType { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    /// <summary>"PendingVerification", "Verified" or "Rejected".</summary>
    public string Status { get; set; } = string.Empty;

    public DateTime UploadedAtUtc { get; set; }

    public DateTime? ReviewedAtUtc { get; set; }

    public string? RejectionReason { get; set; }
}

/// <summary>What the patient is told after a successful upload.</summary>
public class BankTransferReceiptUploadResultDto
{
    public string Message { get; set; } = string.Empty;

    /// <summary>The pharmacy order's status as the pharmacy-order service reported it; uploading never confirms it.</summary>
    public string OrderStatus { get; set; } = string.Empty;

    public MedicinePaymentResponseDto Payment { get; set; } = new();
}

/// <summary>A receipt in the pharmacy staff verification list.</summary>
public class StaffBankTransferReceiptDto
{
    public int Id { get; set; }

    public int PaymentId { get; set; }

    public int OrderId { get; set; }

    public string OrderNumber { get; set; } = string.Empty;

    public decimal Amount { get; set; }

    public string CustomerName { get; set; } = string.Empty;

    public string FileName { get; set; } = string.Empty;

    public string ContentType { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    /// <summary>"PendingVerification", "Verified" or "Rejected".</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>The payment's status: "ReceiptUnderVerification", "Paid" or "ReceiptRejected".</summary>
    public string PaymentStatus { get; set; } = string.Empty;

    public DateTime UploadedAtUtc { get; set; }

    public DateTime? ReviewedAtUtc { get; set; }

    public string? RejectionReason { get; set; }

    /// <summary>"Queued", "Published" or "Failed": whether the verification event reached Kafka.</summary>
    public string VerificationEvent { get; set; } = string.Empty;

    /// <summary>GCN-42: the order's status as last seen here, e.g. "Pending".</summary>
    public string OrderStatus { get; set; } = string.Empty;

    /// <summary>GCN-42: when bank transfer was chosen; a transfer dated earlier does not belong to this order.</summary>
    public DateTime PaymentCreatedAtUtc { get; set; }

    /// <summary>GCN-42: the order's medicines, to review the payment against.</summary>
    public List<MedicinePaymentItemDto> Items { get; set; } = new();

    public string? TransactionReference { get; set; }

    public decimal? TransferredAmount { get; set; }

    public DateTime? TransferDateUtc { get; set; }

    /// <summary>GCN-42: "Queued", "Published" or "Failed" for the approved/rejected event; null before a result.</summary>
    public string? ValidationEvent { get; set; }

    /// <summary>GCN-42: whether the patient has been notified of the result.</summary>
    public bool PatientNotified { get; set; }
}

/// <summary>GCN-42: optional transaction details staff read from the receipt; when given, they are checked too.</summary>
public class ApproveBankTransferDto
{
    public string? TransactionReference { get; set; }

    public decimal? TransferredAmount { get; set; }

    public DateTime? TransferDate { get; set; }
}

public class RejectBankTransferReceiptDto
{
    public string? Reason { get; set; }
}

public class PaymentStaffNotificationDto
{
    public int Id { get; set; }

    /// <summary>The Kafka event it came from, e.g. "BankReceiptUploaded" or "OnlinePaymentSucceeded" (GCN-41).</summary>
    public string NotificationType { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public int OrderId { get; set; }

    public int PaymentId { get; set; }

    public bool IsRead { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}

/// <summary>A stored receipt file for staff to view.</summary>
public sealed record ReceiptFileResult(Stream Content, string ContentType, string FileName);
