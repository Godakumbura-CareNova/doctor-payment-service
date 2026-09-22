using DoctorPayment.Api.Models;

namespace DoctorPayment.Api.Repositories;

/// <summary>A search hit with the availability figures computed in the same query.</summary>
public sealed record DoctorSearchRow(
    DoctorProfile Profile,
    List<string> Degrees,
    int? FirstExperienceYear,
    int AvailableSlotCount,
    DateTime? NextAvailableSlotUtc);

/// <summary>Read-only access to doctor profiles for the patient-facing search and profile view.</summary>
public interface IDoctorSearchRepository
{
    Task<(List<DoctorSearchRow> Items, int TotalCount)> SearchAsync(
        string? search,
        string? specialization,
        bool availableOnly,
        DateTime nowUtc,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>Full profile with qualifications, experience, other details and open upcoming slots.</summary>
    Task<DoctorProfile?> GetPublicProfileAsync(Guid doctorId, DateTime nowUtc, CancellationToken cancellationToken = default);

    /// <summary>Distinct specializations in use, alphabetically, for the search filter.</summary>
    Task<List<string>> GetSpecializationsAsync(CancellationToken cancellationToken = default);
}
