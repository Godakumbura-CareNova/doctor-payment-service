namespace DoctorPayment.Api.Exceptions;

/// <summary>Raised when a fee-change request cannot go ahead in its current state (one already pending, already reviewed).</summary>
public sealed class FeeChangeRequestConflictException : InvalidOperationException
{
    public FeeChangeRequestConflictException(string message)
        : base(message)
    {
    }
}
