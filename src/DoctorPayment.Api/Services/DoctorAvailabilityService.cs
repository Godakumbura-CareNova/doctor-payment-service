using DoctorPayment.Api.DTOs.DoctorAvailability;
using DoctorPayment.Api.Exceptions;
using DoctorPayment.Api.Models;
using DoctorPayment.Api.Repositories;
using DoctorPayment.Api.Services.Interfaces;

namespace DoctorPayment.Api.Services;

public class DoctorAvailabilityService : IDoctorAvailabilityService
{
    /// <summary>Upper bound on open upcoming slots per doctor, so a runaway form cannot flood the table.</summary>
    public const int MaxUpcomingSlots = 200;

    /// <summary>How far ahead a doctor can publish availability.</summary>
    public const int MaxDaysAhead = 90;

    private readonly IDoctorAvailabilityRepository _repository;
    private readonly TimeProvider _timeProvider;

    public DoctorAvailabilityService(IDoctorAvailabilityRepository repository, TimeProvider timeProvider)
    {
        _repository = repository;
        _timeProvider = timeProvider;
    }

    public async Task<List<AvailabilitySlotResponseDto>> GetMineAsync(Guid doctorId, CancellationToken cancellationToken = default)
    {
        var profileId = await _repository.GetProfileIdAsync(doctorId, cancellationToken);
        if (profileId is null) return new List<AvailabilitySlotResponseDto>();

        var slots = await _repository.GetUpcomingSlotsAsync(profileId.Value, Now(), cancellationToken);
        return slots.Select(ToResponse).ToList();
    }

    public async Task<List<AvailabilitySlotResponseDto>> AddAsync(Guid doctorId, CreateAvailabilityRequestDto request, CancellationToken cancellationToken = default)
    {
        if (doctorId == Guid.Empty) throw new ArgumentException("A doctor identity is required.", nameof(doctorId));

        var profileId = await _repository.GetProfileIdAsync(doctorId, cancellationToken)
            ?? throw new DoctorProfileRequiredException();

        var now = Now();
        var start = AsUtc(request.StartUtc);
        var end = AsUtc(request.EndUtc);

        if (start <= now) throw new ArgumentException("Availability must start in the future.", nameof(request));
        if (start > now.AddDays(MaxDaysAhead)) throw new ArgumentException($"Availability can be published at most {MaxDaysAhead} days ahead.", nameof(request));

        var overlapping = await _repository.GetOverlappingSlotsAsync(profileId, start, end, cancellationToken);
        if (overlapping.Count > 0)
        {
            var first = overlapping[0];
            throw new AvailabilityConflictException(
                $"This window overlaps an existing slot ({first.StartUtc:yyyy-MM-dd HH:mm} to {first.EndUtc:HH:mm} UTC). Remove it first or choose another time.");
        }

        var newSlots = BuildSlots(profileId, start, end, TimeSpan.FromMinutes(request.SlotDurationMinutes));
        if (newSlots.Count == 0) throw new ArgumentException("The window is shorter than one slot.", nameof(request));

        var existing = await _repository.GetUpcomingSlotsAsync(profileId, now, cancellationToken);
        var openCount = existing.Count(slot => !slot.IsBooked);
        if (openCount + newSlots.Count > MaxUpcomingSlots)
        {
            throw new AvailabilityConflictException(
                $"You can have at most {MaxUpcomingSlots} open upcoming slots. You already have {openCount}; this window would add {newSlots.Count}.");
        }

        await _repository.AddRangeAsync(newSlots, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        return newSlots.Select(ToResponse).ToList();
    }

    public async Task<bool> DeleteAsync(Guid doctorId, int slotId, CancellationToken cancellationToken = default)
    {
        var profileId = await _repository.GetProfileIdAsync(doctorId, cancellationToken);
        if (profileId is null) return false;

        var slot = await _repository.GetSlotAsync(profileId.Value, slotId, cancellationToken);
        if (slot is null) return false;
        if (slot.IsBooked) throw new AvailabilityConflictException("A patient has already booked this slot, so it cannot be removed.");

        _repository.Remove(slot);
        await _repository.SaveChangesAsync(cancellationToken);
        return true;
    }

    private DateTime Now() => _timeProvider.GetUtcNow().UtcDateTime;

    // JSON without a zone suffix deserialises as Unspecified; treat that as UTC rather than the server's local zone.
    private static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

    private static List<DoctorAvailabilitySlot> BuildSlots(int profileId, DateTime start, DateTime end, TimeSpan duration)
    {
        var slots = new List<DoctorAvailabilitySlot>();
        for (var slotStart = start; slotStart + duration <= end; slotStart += duration)
        {
            slots.Add(new DoctorAvailabilitySlot { DoctorProfileId = profileId, StartUtc = slotStart, EndUtc = slotStart + duration });
        }
        return slots;
    }

    private static AvailabilitySlotResponseDto ToResponse(DoctorAvailabilitySlot slot) => new()
    {
        Id = slot.Id,
        StartUtc = DateTime.SpecifyKind(slot.StartUtc, DateTimeKind.Utc),
        EndUtc = DateTime.SpecifyKind(slot.EndUtc, DateTimeKind.Utc),
        IsBooked = slot.IsBooked
    };
}
