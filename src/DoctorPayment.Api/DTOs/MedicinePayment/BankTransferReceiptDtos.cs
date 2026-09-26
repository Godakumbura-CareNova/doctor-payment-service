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
}

public class RejectBankTransferReceiptDto
{
    public string? Reason { get; set; }
}

public class PaymentStaffNotificationDto
{
    public int Id { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public int OrderId { get; set; }

    public int PaymentId { get; set; }

    public bool IsRead { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}

/// <summary>A stored receipt file for staff to view.</summary>
public sealed record ReceiptFileResult(Stream Content, string ContentType, string FileName);
