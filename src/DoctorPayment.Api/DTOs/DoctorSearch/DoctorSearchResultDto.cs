namespace DoctorPayment.Api.DTOs.DoctorSearch;

public class DoctorSearchResultDto
{
    public List<DoctorSummaryDto> Items { get; set; } = new();

    public int Page { get; set; }

    public int PageSize { get; set; }

    public int TotalCount { get; set; }

    public int TotalPages { get; set; }
}
