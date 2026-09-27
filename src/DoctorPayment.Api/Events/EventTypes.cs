namespace DoctorPayment.Api.Events;

public static class EventTypes
{
    /// <summary>GCN-40: bank transfer receipt uploaded, verified or rejected.</summary>
    public const string PaymentTopic = "carenova.payment.events.v1";

    /// <summary>GCN-43: a medicine order's status changed (ready for collection, rejected, collected).</summary>
    public const string OrderStatusTopic = "carenova.order-status.events.v1";

    public const string BankReceiptUploaded = "BankReceiptUploaded";
    public const string BankReceiptVerified = "BankReceiptVerified";
    public const string BankReceiptRejected = "BankReceiptRejected";

    public const string MedicineOrderStatusUpdated = "MedicineOrderStatusUpdated";
}
