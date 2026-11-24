using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CompileCare.Infrastructure.Persistence.Configurations;

public sealed class ProviderAvailabilitySessionConfiguration : IEntityTypeConfiguration<ProviderAvailabilitySession>
{
    public void Configure(EntityTypeBuilder<ProviderAvailabilitySession> builder)
    {
        builder.ToTable("provider_availability_sessions", table =>
        {
            table.HasCheckConstraint("CK_provider_availability_sessions_time_range", "\"EndsAtUtc\" > \"StartsAtUtc\"");
            table.HasCheckConstraint("CK_provider_availability_sessions_slot_duration", "\"SlotDurationMinutes\" BETWEEN 5 AND 120");
        });
        builder.Property(x => x.TimeZoneId).HasMaxLength(100).IsRequired();
        builder.HasAlternateKey(x => new { x.TenantId, x.Id });
        builder.HasIndex(x => new { x.TenantId, x.HospitalId, x.DepartmentId, x.LocalDate });
        builder.HasIndex(x => new { x.TenantId, x.ProviderStaffId, x.LocalDate, x.StartsAtUtc, x.EndsAtUtc });
        builder.HasOne<Hospital>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.HospitalId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Department>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.HospitalId, x.DepartmentId })
            .HasPrincipalKey(x => new { x.TenantId, x.HospitalId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<StaffProfile>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.ProviderStaffId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class AppointmentConfiguration : IEntityTypeConfiguration<Appointment>
{
    public void Configure(EntityTypeBuilder<Appointment> builder)
    {
        builder.ToTable("appointments", table =>
        {
            table.HasCheckConstraint("CK_appointments_time_range", "\"SlotEndsAtUtc\" > \"SlotStartsAtUtc\"");
            table.HasCheckConstraint("CK_appointments_status", "\"Status\" IN ('Booked', 'Cancelled', 'Completed', 'NoShow')");
        });
        builder.Property(x => x.Status).HasMaxLength(20).IsRequired();
        builder.Property(x => x.Reason).HasMaxLength(250);
        builder.HasAlternateKey(x => new { x.TenantId, x.Id });
        builder.HasIndex(x => new { x.TenantId, x.AvailabilitySessionId, x.SlotStartsAtUtc })
            .IsUnique().HasFilter("\"Status\" = 'Booked'");
        builder.HasIndex(x => new { x.TenantId, x.HospitalId, x.DepartmentId });
        builder.HasIndex(x => new { x.TenantId, x.HospitalId, x.PatientId });
        builder.HasIndex(x => new { x.TenantId, x.ProviderStaffId });
        builder.HasIndex(x => new { x.TenantId, x.PatientId, x.SlotStartsAtUtc })
            .HasFilter("\"Status\" = 'Booked'");
        builder.HasIndex(x => new { x.TenantId, x.RescheduledFromAppointmentId });
        builder.HasOne<Appointment>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.RescheduledFromAppointmentId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_appointments_rescheduled_from");
        builder.HasOne<ProviderAvailabilitySession>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.AvailabilitySessionId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<HospitalPatientRegistration>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.HospitalId, x.PatientId })
            .HasPrincipalKey(x => new { x.TenantId, x.HospitalId, x.PatientId })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Hospital>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.HospitalId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Department>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.HospitalId, x.DepartmentId })
            .HasPrincipalKey(x => new { x.TenantId, x.HospitalId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<StaffProfile>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.ProviderStaffId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
