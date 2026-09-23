namespace DoctorPayment.Api.Exceptions;

/// <summary>Raised when a requested change clashes with the doctor's existing slots (overlap, booked slot, too many slots).</summary>
public sealed class AvailabilityConflictException : InvalidOperationException
{
    public AvailabilityConflictException(string message)
        : base(message)
    {
    }
}
