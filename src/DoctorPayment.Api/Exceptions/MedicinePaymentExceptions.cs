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

/// <summary>Two requests chose a method for the same order at once; the unique order index refused one of them.</summary>
public sealed class MedicinePaymentConflictException : InvalidOperationException
{
    public MedicinePaymentConflictException(string message)
        : base(message)
    {
    }
}
