using DoctorPayment.Api.DTOs.DoctorProfile;
using DoctorPayment.Api.Models;
using DoctorPayment.Api.Repositories;
using DoctorPayment.Api.Services.Interfaces;

namespace DoctorPayment.Api.Services;

public class DoctorProfileService : IDoctorProfileService
{
    private readonly IDoctorProfileRepository _repository;

    public DoctorProfileService(IDoctorProfileRepository repository) => _repository = repository;

    public async Task<DoctorProfileResponseDto?> GetByDoctorIdAsync(Guid doctorId, CancellationToken cancellationToken = default)
    {
        var profile = await _repository.GetByDoctorIdAsync(doctorId, cancellationToken);
        return profile is null ? null : ToResponse(profile);
    }

    public async Task<DoctorProfileResponseDto> SaveAsync(Guid doctorId, UpdateDoctorProfileRequestDto request, CancellationToken cancellationToken = default)
    {
        if (doctorId == Guid.Empty) throw new ArgumentException("A doctor identity is required.", nameof(doctorId));

        var now = DateTime.UtcNow;
        var profile = await _repository.GetByDoctorIdAsync(doctorId, cancellationToken);

        if (profile is null)
        {
            profile = new DoctorProfile { DoctorId = doctorId, CreatedAtUtc = now };
            await _repository.AddAsync(profile, cancellationToken);
        }

        profile.Specialization = Normalize(request.Specialization);
        profile.RegistrationNumber = Normalize(request.RegistrationNumber).ToUpperInvariant();
        profile.Biography = string.IsNullOrWhiteSpace(request.Biography) ? null : request.Biography.Trim();
        profile.UpdatedAtUtc = now;

        // The request carries the full lists, so the stored collections are replaced as a whole.
        profile.Qualifications.Clear();
        profile.Qualifications.AddRange(request.Qualifications.Select(item => new DoctorQualification
        {
            Degree = Normalize(item.Degree),
            Institution = Normalize(item.Institution),
            StartYear = item.StartYear,
            EndYear = item.EndYear
        }));

        profile.Experiences.Clear();
        profile.Experiences.AddRange(request.Experiences.Select(item => new DoctorExperience
        {
            Position = Normalize(item.Position),
            Organization = Normalize(item.Organization),
            StartYear = item.StartYear,
            EndYear = item.EndYear
        }));

        profile.OtherDetails.Clear();
        profile.OtherDetails.AddRange(request.OtherDetails.Select(item => new DoctorOtherDetail
        {
            Title = Normalize(item.Title),
            Details = string.IsNullOrWhiteSpace(item.Details) ? null : item.Details.Trim(),
            StartYear = item.StartYear,
            EndYear = item.EndYear
        }));

        await _repository.SaveChangesAsync(cancellationToken);
        return ToResponse(profile);
    }

    public async Task<bool> DeleteAsync(Guid doctorId, CancellationToken cancellationToken = default)
    {
        var profile = await _repository.GetByDoctorIdAsync(doctorId, cancellationToken);
        if (profile is null) return false;

        _repository.Remove(profile);
        await _repository.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static string Normalize(string? value) => (value ?? string.Empty).Trim();

    private static DoctorProfileResponseDto ToResponse(DoctorProfile profile) => new()
    {
        Id = profile.Id,
        DoctorId = profile.DoctorId,
        Specialization = profile.Specialization,
        RegistrationNumber = profile.RegistrationNumber,
        Biography = profile.Biography,
        CreatedAtUtc = profile.CreatedAtUtc,
        UpdatedAtUtc = profile.UpdatedAtUtc,
        Qualifications = profile.Qualifications
            .OrderBy(item => item.StartYear).ThenBy(item => item.Id)
            .Select(item => new QualificationResponseDto
            {
                Id = item.Id,
                Degree = item.Degree,
                Institution = item.Institution,
                StartYear = item.StartYear,
                EndYear = item.EndYear
            })
            .ToList(),
        Experiences = profile.Experiences
            .OrderBy(item => item.StartYear).ThenBy(item => item.Id)
            .Select(item => new ExperienceResponseDto
            {
                Id = item.Id,
                Position = item.Position,
                Organization = item.Organization,
                StartYear = item.StartYear,
                EndYear = item.EndYear
            })
            .ToList(),
        OtherDetails = profile.OtherDetails
            .OrderBy(item => item.StartYear).ThenBy(item => item.Id)
            .Select(item => new OtherDetailResponseDto
            {
                Id = item.Id,
                Title = item.Title,
                Details = item.Details,
                StartYear = item.StartYear,
                EndYear = item.EndYear
            })
            .ToList()
    };
}
