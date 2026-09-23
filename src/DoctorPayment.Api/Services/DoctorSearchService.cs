using DoctorPayment.Api.DTOs.DoctorProfile;
using DoctorPayment.Api.DTOs.DoctorSearch;
using DoctorPayment.Api.Repositories;
using DoctorPayment.Api.Services.Interfaces;

namespace DoctorPayment.Api.Services;

public class DoctorSearchService : IDoctorSearchService
{
    /// <summary>How many open slots the public profile lists; the count still reflects all of them.</summary>
    public const int UpcomingSlotsShown = 10;

    private readonly IDoctorSearchRepository _repository;
    private readonly TimeProvider _timeProvider;

    public DoctorSearchService(IDoctorSearchRepository repository, TimeProvider timeProvider)
    {
        _repository = repository;
        _timeProvider = timeProvider;
    }

    public async Task<DoctorSearchResultDto> SearchAsync(DoctorSearchQueryDto query, CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var page = Math.Max(query.Page, 1);
        var pageSize = Math.Clamp(query.PageSize, 1, DoctorSearchQueryDto.MaxPageSize);

        var (rows, totalCount) = await _repository.SearchAsync(
            Normalize(query.Search),
            Normalize(query.Specialization),
            query.AvailableOnly,
            now,
            page,
            pageSize,
            cancellationToken);

        return new DoctorSearchResultDto
        {
            Items = rows.Select(row => ToSummary(row, now.Year)).ToList(),
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            TotalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize)
        };
    }

    public async Task<DoctorPublicProfileDto?> GetPublicProfileAsync(Guid doctorId, CancellationToken cancellationToken = default)
    {
        if (doctorId == Guid.Empty) return null;

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var profile = await _repository.GetPublicProfileAsync(doctorId, now, cancellationToken);
        if (profile is null) return null;

        // The repository already filtered the slots down to open, upcoming ones.
        var openSlots = profile.AvailabilitySlots.OrderBy(slot => slot.StartUtc).ThenBy(slot => slot.Id).ToList();

        return new DoctorPublicProfileDto
        {
            DoctorId = profile.DoctorId,
            FullName = profile.FullName,
            Specialization = profile.Specialization,
            RegistrationNumber = profile.RegistrationNumber,
            Biography = profile.Biography,
            ConsultationFee = profile.ConsultationFee,
            ProfilePicturePath = profile.ProfilePicturePath,
            YearsOfExperience = YearsOfExperience(profile.Experiences.Select(item => (int?)item.StartYear).Min(), now.Year),
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
                .ToList(),
            Availability = new DoctorAvailabilityDto
            {
                HasAvailableSlots = openSlots.Count > 0,
                AvailableSlotCount = openSlots.Count,
                NextAvailableSlotUtc = openSlots.FirstOrDefault()?.StartUtc
            },
            UpcomingSlots = openSlots
                .Take(UpcomingSlotsShown)
                .Select(slot => new AvailabilitySlotDto { Id = slot.Id, StartUtc = slot.StartUtc, EndUtc = slot.EndUtc })
                .ToList()
        };
    }

    public Task<List<string>> GetSpecializationsAsync(CancellationToken cancellationToken = default) =>
        _repository.GetSpecializationsAsync(cancellationToken);

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static int? YearsOfExperience(int? firstYear, int currentYear) =>
        firstYear is null ? null : Math.Max(currentYear - firstYear.Value, 0);

    private static DoctorSummaryDto ToSummary(DoctorSearchRow row, int currentYear) => new()
    {
        DoctorId = row.Profile.DoctorId,
        FullName = row.Profile.FullName,
        Specialization = row.Profile.Specialization,
        RegistrationNumber = row.Profile.RegistrationNumber,
        Biography = row.Profile.Biography,
        ConsultationFee = row.Profile.ConsultationFee,
        ProfilePicturePath = row.Profile.ProfilePicturePath,
        Qualifications = row.Degrees,
        YearsOfExperience = YearsOfExperience(row.FirstExperienceYear, currentYear),
        Availability = new DoctorAvailabilityDto
        {
            HasAvailableSlots = row.AvailableSlotCount > 0,
            AvailableSlotCount = row.AvailableSlotCount,
            NextAvailableSlotUtc = row.NextAvailableSlotUtc
        }
    };
}
