namespace DoctorPayment.Api.DTOs.FeeChangeRequest;

/// <summary>An admin rejecting a fee-change request; the reason is shown to the doctor.</summary>
public class RejectFeeChangeRequestDto
{
    public const int MinReasonLength = 5;
    public const int MaxReasonLength = 500;

    public string? Reason { get; set; }
}
