using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HeapCare.Infrastructure.Persistence.Configurations;

public sealed class QueueEntryConfiguration : IEntityTypeConfiguration<QueueEntry>
{
    public void Configure(EntityTypeBuilder<QueueEntry> builder)
    {
        builder.ToTable("queue_entries", table =>
        {
            table.HasCheckConstraint("CK_queue_entries_token_positive", "\"TokenNumber\" > 0");
            table.HasCheckConstraint("CK_queue_entries_status", "\"Status\" IN ('Waiting', 'Called', 'InProgress', 'Completed', 'Cancelled', 'NoShow')");
            table.HasCheckConstraint("CK_queue_entries_closure_reason", "\"ClosureReason\" IS NULL OR length(trim(\"ClosureReason\")) BETWEEN 1 AND 250");
        });
        builder.Property(x => x.Status).HasMaxLength(20).IsRequired();
        builder.Property(x => x.TimeZoneId).HasMaxLength(100).IsRequired();
        builder.Property(x => x.ClosureReason).HasMaxLength(250);
        builder.HasAlternateKey(x => new { x.TenantId, x.Id });
        builder.HasAlternateKey(x => new { x.TenantId, x.HospitalId, x.Id });
        builder.HasIndex(x => new { x.TenantId, x.HospitalId, x.DepartmentId, x.QueueDateLocal, x.TokenNumber })
            .HasDatabaseName("IX_queue_entries_daily_token").IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.HospitalId, x.DepartmentId, x.QueueDateLocal, x.Status, x.TokenNumber })
            .HasDatabaseName("IX_queue_entries_daily_status_token");
        builder.HasIndex(x => new { x.TenantId, x.HospitalId, x.PatientId });
        builder.HasIndex(x => new { x.TenantId, x.AppointmentId }).IsUnique().HasFilter("\"AppointmentId\" IS NOT NULL");
        builder.HasOne<Hospital>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.HospitalId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Department>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.HospitalId, x.DepartmentId })
            .HasPrincipalKey(x => new { x.TenantId, x.HospitalId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<HospitalPatientRegistration>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.HospitalId, x.PatientId })
            .HasPrincipalKey(x => new { x.TenantId, x.HospitalId, x.PatientId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Appointment>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.AppointmentId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
