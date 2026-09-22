using DoctorPayment.Api.DTOs.DoctorSearch;

namespace DoctorPayment.Api.Services.Interfaces;

public interface IDoctorSearchService
{
    /// <summary>Pages through doctors matching the criteria; an empty result is a normal outcome, not an error.</summary>
    Task<DoctorSearchResultDto> SearchAsync(DoctorSearchQueryDto query, CancellationToken cancellationToken = default);

    /// <summary>The patient-facing profile of one doctor, or null when that doctor has not published a profile.</summary>
    Task<DoctorPublicProfileDto?> GetPublicProfileAsync(Guid doctorId, CancellationToken cancellationToken = default);

    Task<List<string>> GetSpecializationsAsync(CancellationToken cancellationToken = default);
}
