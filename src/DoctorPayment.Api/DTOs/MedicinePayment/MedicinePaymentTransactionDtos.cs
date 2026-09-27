namespace DoctorPayment.Api.DTOs.MedicinePayment;

/// <summary>One medicine payment transaction in the staff list.</summary>
public class MedicinePaymentTransactionSummaryDto
{
    public int Id { get; set; }

    public int OrderId { get; set; }

    public string OrderNumber { get; set; } = string.Empty;

    public string CustomerName { get; set; } = string.Empty;

    public decimal Amount { get; set; }

    /// <summary>"Online", "BankTransfer" or "Cash".</summary>
    public string Method { get; set; } = string.Empty;

    /// <summary>The exact payment status, e.g. "PayOnCollection" or "ReceiptUnderVerification".</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>"Pending", "Successful", "Failed" or "Rejected".</summary>
    public string Category { get; set; } = string.Empty;

    public int ItemCount { get; set; }

    /// <summary>The pharmacy order's status: "Pending", "ReadyForCollection", "Collected" or "Rejected".</summary>
    public string OrderStatus { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}

/// <summary>How many transactions are in each category, for the monitoring tiles.</summary>
public class MedicinePaymentTransactionCountsDto
{
    public int All { get; set; }

    public int Pending { get; set; }

    public int Successful { get; set; }

    public int Failed { get; set; }

    public int Rejected { get; set; }
}

public class MedicinePaymentTransactionListDto
{
    public List<MedicinePaymentTransactionSummaryDto> Transactions { get; set; } = new();

    /// <summary>Counts over all transactions, regardless of the filter.</summary>
    public MedicinePaymentTransactionCountsDto Counts { get; set; } = new();
}

public class MedicinePaymentItemDto
{
    public string MedicineName { get; set; } = string.Empty;

    public int Quantity { get; set; }

    public decimal UnitPrice { get; set; }

    public decimal LineTotal { get; set; }
}

public class MedicinePaymentStatusChangeDto
{
    public string? FromStatus { get; set; }

    public string ToStatus { get; set; } = string.Empty;

    public string ChangedByRole { get; set; } = string.Empty;

    public string? Note { get; set; }

    public DateTime ChangedAtUtc { get; set; }
}

/// <summary>A transaction with its medicine-order information, receipts and status history.</summary>
public class MedicinePaymentTransactionDetailDto : MedicinePaymentTransactionSummaryDto
{
    public Guid PatientId { get; set; }

    public List<MedicinePaymentItemDto> Items { get; set; } = new();

    public List<BankTransferReceiptDto> Receipts { get; set; } = new();

    public List<MedicinePaymentStatusChangeDto> History { get; set; } = new();

    /// <summary>The statuses staff may move this payment to now; empty when it cannot be changed here or for admins.</summary>
    public List<string> AllowedStatusChanges { get; set; } = new();

    /// <summary>Order status changes made through payment management, oldest first.</summary>
    public List<MedicineOrderStatusChangeDto> OrderStatusHistory { get; set; } = new();

    /// <summary>The order statuses staff may set now; empty when none apply or for admins.</summary>
    public List<string> AllowedOrderStatusChanges { get; set; } = new();

    /// <summary>False for admins, who can view but not change statuses.</summary>
    public bool CanManage { get; set; }
}

public class MedicineOrderStatusChangeDto
{
    public string FromStatus { get; set; } = string.Empty;

    public string ToStatus { get; set; } = string.Empty;

    public string? Reason { get; set; }

    public DateTime ChangedAtUtc { get; set; }

    /// <summary>"Queued", "Published" or "Failed": whether the Kafka event reached Kafka.</summary>
    public string EventStatus { get; set; } = string.Empty;

    /// <summary>"Pending", "Delivered" or "Failed": whether the patient was notified.</summary>
    public string NotificationStatus { get; set; } = string.Empty;

    public string? NotificationError { get; set; }
}

public class UpdateMedicineOrderStatusRequestDto
{
    /// <summary>"ReadyForCollection", "Rejected" or "Collected".</summary>
    public string? Status { get; set; }

    /// <summary>Required when rejecting; shown to the patient.</summary>
    public string? Reason { get; set; }
}

public class UpdateMedicinePaymentStatusRequestDto
{
    /// <summary>"Paid" or "Failed".</summary>
    public string? Status { get; set; }

    /// <summary>Required when marking a payment as failed.</summary>
    public string? Note { get; set; }
}
