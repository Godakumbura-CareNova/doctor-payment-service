using DoctorPayment.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace DoctorPayment.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public DbSet<DoctorProfile> DoctorProfiles { get; set; }

    public DbSet<DoctorQualification> DoctorQualifications { get; set; }

    public DbSet<DoctorExperience> DoctorExperiences { get; set; }

    public DbSet<DoctorOtherDetail> DoctorOtherDetails { get; set; }

    public DbSet<DoctorAvailabilitySlot> DoctorAvailabilitySlots { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<DoctorProfile>()
            .HasIndex(profile => profile.DoctorId)
            .IsUnique();

        modelBuilder.Entity<DoctorProfile>()
            .Property(profile => profile.ConsultationFee)
            .HasPrecision(10, 2);

        modelBuilder.Entity<DoctorProfile>()
            .HasMany(profile => profile.Qualifications)
            .WithOne(qualification => qualification.DoctorProfile)
            .HasForeignKey(qualification => qualification.DoctorProfileId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<DoctorProfile>()
            .HasMany(profile => profile.Experiences)
            .WithOne(experience => experience.DoctorProfile)
            .HasForeignKey(experience => experience.DoctorProfileId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<DoctorProfile>()
            .HasMany(profile => profile.OtherDetails)
            .WithOne(detail => detail.DoctorProfile)
            .HasForeignKey(detail => detail.DoctorProfileId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<DoctorProfile>()
            .HasMany(profile => profile.AvailabilitySlots)
            .WithOne(slot => slot.DoctorProfile)
            .HasForeignKey(slot => slot.DoctorProfileId)
            .OnDelete(DeleteBehavior.Cascade);

        // Search filters on these, so they get indexes.
        modelBuilder.Entity<DoctorProfile>().HasIndex(profile => profile.FullName);
        modelBuilder.Entity<DoctorProfile>().HasIndex(profile => profile.Specialization);
        modelBuilder.Entity<DoctorAvailabilitySlot>().HasIndex(slot => new { slot.DoctorProfileId, slot.StartUtc, slot.IsBooked });
    }
}
