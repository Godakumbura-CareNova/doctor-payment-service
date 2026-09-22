using DoctorPayment.Api.DTOs.DoctorProfile;

namespace DoctorPayment.Api.Services.Interfaces;

public interface IDoctorProfileService
{
    /// <summary>Returns the professional profile of the given doctor, or null when none has been created yet.</summary>
    Task<DoctorProfileResponseDto?> GetByDoctorIdAsync(Guid doctorId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates the profile on first save and fully replaces it on later saves.
    /// <paramref name="fullName"/> is the doctor's name from the JWT; it is stored so patients can search by name.
    /// </summary>
    Task<DoctorProfileResponseDto> SaveAsync(Guid doctorId, string? fullName, UpdateDoctorProfileRequestDto request, CancellationToken cancellationToken = default);

    /// <summary>Deletes the whole professional profile (qualifications and experience included). Returns false when none exists.</summary>
    Task<bool> DeleteAsync(Guid doctorId, CancellationToken cancellationToken = default);
}
