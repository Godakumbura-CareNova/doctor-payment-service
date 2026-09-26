namespace DoctorPayment.Api.Exceptions;

/// <summary>The pharmacy-order service could not be reached or answered with an error, so the order cannot be checked.</summary>
public sealed class OrderServiceUnavailableException : Exception
{
    public OrderServiceUnavailableException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

/// <summary>The order exists but cannot be paid for (rejected, collected, or missing its items or total).</summary>
public sealed class InvalidMedicineOrderException : Exception
{
    public InvalidMedicineOrderException(string message)
        : base(message)
    {
    }
}

/// <summary>The chosen payment method is switched off, not configured, or not allowed for this order.</summary>
public sealed class PaymentMethodUnavailableException : InvalidOperationException
{
    public PaymentMethodUnavailableException(string message)
        : base(message)
    {
    }
}

/// <summary>The receipt file is missing, not a JPG/JPEG/PNG, or larger than the limit. Nothing was stored.</summary>
public sealed class InvalidReceiptFileException : ArgumentException
{
    public InvalidReceiptFileException(string message)
        : base(message)
    {
    }
}

/// <summary>The receipt cannot be tied to this order's bank transfer payment (wrong method, amount or state).</summary>
public sealed class ReceiptAssociationException : Exception
{
    public ReceiptAssociationException(string message)
        : base(message)
    {
    }
}

/// <summary>Storing the receipt failed for a technical reason. Nothing was recorded, so the patient can retry.</summary>
public sealed class ReceiptUploadFailedException : Exception
{
    public ReceiptUploadFailedException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

/// <summary>The receipt was already reviewed, or another upload or review changed it first.</summary>
public sealed class ReceiptReviewConflictException : InvalidOperationException
{
    public ReceiptReviewConflictException(string message)
        : base(message)
    {
    }
}

/// <summary>Two requests chose a method for the same order at once; the unique order index refused one of them.</summary>
public sealed class MedicinePaymentConflictException : InvalidOperationException
{
    public MedicinePaymentConflictException(string message)
        : base(message)
    {
    }
}
