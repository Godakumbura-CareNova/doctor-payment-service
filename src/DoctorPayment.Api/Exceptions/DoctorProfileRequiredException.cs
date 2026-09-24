namespace DoctorPayment.Api.Exceptions;

/// <summary>Raised when a doctor tries to manage availability or their fee before publishing a professional profile.</summary>
public sealed class DoctorProfileRequiredException : InvalidOperationException
{
    public DoctorProfileRequiredException()
        : base("Create your professional profile before adding consultation availability.")
    {
    }

    public DoctorProfileRequiredException(string message)
        : base(message)
    {
    }
}
