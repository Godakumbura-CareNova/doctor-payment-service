namespace DoctorPayment.Api.Events;

public static class EventTypes
{
    /// <summary>GCN-40: bank transfer receipt uploaded, verified or rejected.</summary>
    public const string PaymentTopic = "carenova.payment.events.v1";

    /// <summary>GCN-43: a medicine order's status changed (ready for collection, rejected, collected).</summary>
    public const string OrderStatusTopic = "carenova.order-status.events.v1";

    /// <summary>GCN-42: the result of staff validating a bank transfer transaction (approved or rejected).</summary>
    public const string BankTransferValidationTopic = "carenova.bank-transfer-validation.events.v1";

    public const string BankReceiptUploaded = "BankReceiptUploaded";
    public const string BankReceiptVerified = "BankReceiptVerified";
    public const string BankReceiptRejected = "BankReceiptRejected";

    public const string MedicineOrderStatusUpdated = "MedicineOrderStatusUpdated";

    public const string BankTransferApproved = "BankTransferApproved";
    public const string BankTransferRejected = "BankTransferRejected";
}
