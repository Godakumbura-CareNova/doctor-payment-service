namespace DoctorPayment.Api.Events;

public static class EventTypes
{
    public const string PaymentTopic = "carenova.payment.events.v1";

    public const string BankReceiptUploaded = "BankReceiptUploaded";
    public const string BankReceiptVerified = "BankReceiptVerified";
    public const string BankReceiptRejected = "BankReceiptRejected";
}
