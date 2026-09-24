namespace DoctorPayment.Api.DTOs.FeeChangeRequest;

/// <summary>A doctor asking for a new consultation fee. Nullable so a missing fee is reported as "required", not as 0.</summary>
public class CreateFeeChangeRequestDto
{
    public const decimal MaxFee = 1_000_000m;

    public decimal? RequestedFee { get; set; }
}
