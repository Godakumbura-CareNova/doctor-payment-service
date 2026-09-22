namespace DoctorPayment.Api.DTOs.DoctorSearch;

/// <summary>Query-string criteria a patient uses to find doctors. Every field is optional; no criteria lists everyone.</summary>
public class DoctorSearchQueryDto
{
    public const int DefaultPageSize = 12;
    public const int MaxPageSize = 50;

    /// <summary>Free text matched against the doctor's name and specialization.</summary>
    public string? Search { get; set; }

    /// <summary>Exact specialization filter (case-insensitive), usually chosen from the specializations list.</summary>
    public string? Specialization { get; set; }

    /// <summary>When true only doctors with at least one open upcoming consultation slot are returned.</summary>
    public bool AvailableOnly { get; set; }

    public int Page { get; set; } = 1;

    public int PageSize { get; set; } = DefaultPageSize;
}
