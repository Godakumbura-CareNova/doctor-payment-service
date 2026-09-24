namespace DoctorPayment.Api.DTOs.FeeChangeRequest;

/// <summary>One fee-change request as the doctor sees it.</summary>
public class FeeChangeRequestResponseDto
{
    public int Id { get; set; }

    /// <summary>The fee when the request was submitted; null when none was set.</summary>
    public decimal? CurrentFee { get; set; }

    public decimal RequestedFee { get; set; }

    /// <summary>"Pending", "Approved" or "Rejected".</summary>
    public string Status { get; set; } = string.Empty;

    public string? RejectionReason { get; set; }

    public DateTime SubmittedAtUtc { get; set; }

    public DateTime? ReviewedAtUtc { get; set; }
}

/// <summary>What the doctor's fee section shows: the live fee, any pending request and recent history.</summary>
public class MyFeeChangeRequestsDto
{
    public decimal? CurrentFee { get; set; }

    public FeeChangeRequestResponseDto? PendingRequest { get; set; }

    /// <summary>Most recent first.</summary>
    public List<FeeChangeRequestResponseDto> Requests { get; set; } = new();
}

/// <summary>A request as the admin reviews it, with who asked and what they charge today.</summary>
public class AdminFeeChangeRequestDto : FeeChangeRequestResponseDto
{
    public Guid DoctorId { get; set; }

    public string DoctorName { get; set; } = string.Empty;

    public string Specialization { get; set; } = string.Empty;

    /// <summary>The doctor's fee right now (may differ from <see cref="FeeChangeRequestResponseDto.CurrentFee"/> once approved).</summary>
    public decimal? DoctorCurrentFee { get; set; }
}
