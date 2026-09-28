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

    public DbSet<ConsultationFeeChangeRequest> ConsultationFeeChangeRequests { get; set; }

    public DbSet<DoctorNotification> DoctorNotifications { get; set; }

    public DbSet<MedicinePayment> MedicinePayments { get; set; }

    public DbSet<BankTransferReceipt> BankTransferReceipts { get; set; }

    public DbSet<MedicinePaymentItem> MedicinePaymentItems { get; set; }

    public DbSet<MedicinePaymentStatusChange> MedicinePaymentStatusChanges { get; set; }

    public DbSet<MedicineOrderStatusChange> MedicineOrderStatusChanges { get; set; }

    public DbSet<PatientNotification> PatientNotifications { get; set; }

    public DbSet<PaymentStaffNotification> PaymentStaffNotifications { get; set; }

    public DbSet<EventOutbox> EventOutboxEntries { get; set; }

    public DbSet<OnlinePaymentAttempt> OnlinePaymentAttempts { get; set; }

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

        modelBuilder.Entity<ConsultationFeeChangeRequest>(request =>
        {
            request.HasOne(item => item.DoctorProfile)
                .WithMany()
                .HasForeignKey(item => item.DoctorProfileId)
                .OnDelete(DeleteBehavior.Cascade);
            request.Property(item => item.CurrentFee).HasPrecision(10, 2);
            request.Property(item => item.RequestedFee).HasPrecision(10, 2);
            request.Property(item => item.Status).HasConversion<string>().HasMaxLength(20);
            // At most one pending request per doctor; see PendingDoctorProfileId.
            request.HasIndex(item => item.PendingDoctorProfileId).IsUnique();
            request.HasIndex(item => new { item.DoctorProfileId, item.SubmittedAtUtc });
            request.HasIndex(item => new { item.Status, item.SubmittedAtUtc });
        });

        modelBuilder.Entity<DoctorNotification>(notification =>
        {
            notification.HasOne(item => item.DoctorProfile)
                .WithMany()
                .HasForeignKey(item => item.DoctorProfileId)
                .OnDelete(DeleteBehavior.Cascade);
            notification.HasIndex(item => new { item.DoctorProfileId, item.CreatedAtUtc });
        });

        modelBuilder.Entity<MedicinePayment>(payment =>
        {
            payment.Property(item => item.Amount).HasPrecision(10, 2);
            payment.Property(item => item.Method).HasConversion<string>().HasMaxLength(20);
            payment.Property(item => item.Status).HasConversion<string>().HasMaxLength(30);
            // One payment per pharmacy order; choosing again updates it.
            payment.HasIndex(item => item.OrderId).IsUnique();
            payment.HasIndex(item => item.PatientId);
            payment.HasMany(item => item.Receipts)
                .WithOne(receipt => receipt.MedicinePayment)
                .HasForeignKey(receipt => receipt.MedicinePaymentId)
                .OnDelete(DeleteBehavior.Cascade);
            payment.HasMany(item => item.Items)
                .WithOne()
                .HasForeignKey(item => item.MedicinePaymentId)
                .OnDelete(DeleteBehavior.Cascade);
            payment.HasMany(item => item.StatusHistory)
                .WithOne()
                .HasForeignKey(change => change.MedicinePaymentId)
                .OnDelete(DeleteBehavior.Cascade);
            payment.HasMany(item => item.OrderStatusHistory)
                .WithOne()
                .HasForeignKey(change => change.MedicinePaymentId)
                .OnDelete(DeleteBehavior.Cascade);
            // The staff transaction list is filtered by status and sorted by the latest change.
            payment.HasIndex(item => new { item.Status, item.UpdatedAtUtc });
        });

        modelBuilder.Entity<MedicinePaymentItem>().Property(item => item.UnitPrice).HasPrecision(10, 2);

        modelBuilder.Entity<MedicineOrderStatusChange>(change =>
        {
            change.Property(item => item.NotificationStatus).HasConversion<string>().HasMaxLength(20);
            change.HasIndex(item => item.EventId).IsUnique();
            change.HasIndex(item => new { item.MedicinePaymentId, item.ChangedAtUtc });
        });

        modelBuilder.Entity<PatientNotification>(notification =>
        {
            notification.HasIndex(item => item.EventId).IsUnique();
            notification.HasIndex(item => new { item.PatientId, item.CreatedAtUtc });
        });

        modelBuilder.Entity<MedicinePaymentStatusChange>(change =>
        {
            change.Property(item => item.FromStatus).HasConversion<string>().HasMaxLength(30);
            change.Property(item => item.ToStatus).HasConversion<string>().HasMaxLength(30);
            change.HasIndex(item => new { item.MedicinePaymentId, item.ChangedAtUtc });
        });

        modelBuilder.Entity<BankTransferReceipt>(receipt =>
        {
            receipt.Property(item => item.Status).HasConversion<string>().HasMaxLength(30);
            // At most one receipt waiting for verification per payment; see PendingPaymentId.
            receipt.HasIndex(item => item.PendingPaymentId).IsUnique();
            receipt.HasIndex(item => new { item.Status, item.UploadedAtUtc });
            receipt.HasIndex(item => item.OrderId);
        });

        // GCN-41: online card payment attempts. One gateway payment per attempt, numbered per medicine payment.
        modelBuilder.Entity<OnlinePaymentAttempt>(attempt =>
        {
            attempt.Property(item => item.Status).HasConversion<string>().HasMaxLength(20);
            attempt.Property(item => item.Amount).HasPrecision(18, 2);
            attempt.HasOne(item => item.MedicinePayment).WithMany().HasForeignKey(item => item.MedicinePaymentId).OnDelete(DeleteBehavior.Cascade);
            attempt.HasIndex(item => item.GatewayReference).IsUnique();
            attempt.HasIndex(item => new { item.MedicinePaymentId, item.AttemptNumber }).IsUnique();
        });

        modelBuilder.Entity<PaymentStaffNotification>(notification =>
        {
            notification.HasIndex(item => item.EventId).IsUnique();
            notification.HasIndex(item => item.CreatedAtUtc);
        });

        modelBuilder.Entity<EventOutbox>(entry =>
        {
            entry.HasIndex(item => item.EventId).IsUnique();
            entry.Property(item => item.Topic).HasMaxLength(200);
            entry.Property(item => item.EventType).HasMaxLength(100);
            entry.Property(item => item.EventKey).HasMaxLength(100);
            entry.HasIndex(item => new { item.PublishedAtUtc, item.PublishAttempts });
        });
    }
}
