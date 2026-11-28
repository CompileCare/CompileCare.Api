using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HeapCare.Infrastructure.Persistence.Configurations;

public sealed class InpatientAdmissionConfiguration : IEntityTypeConfiguration<InpatientAdmission>
{
    public void Configure(EntityTypeBuilder<InpatientAdmission> b)
    {
        b.ToTable("inpatient_admissions", table =>
        {
            table.HasCheckConstraint(
                "CK_inpatient_admissions_status",
                "\"Status\" IN ('Active', 'Cancelled', 'Discharged')");
            table.HasCheckConstraint(
                "CK_inpatient_admissions_lifecycle",
                """
                ("Status" = 'Active' AND "CancelledAtUtc" IS NULL AND "DischargedAtUtc" IS NULL)
                OR ("Status" = 'Cancelled' AND "CancelledAtUtc" IS NOT NULL AND "DischargedAtUtc" IS NULL)
                OR ("Status" = 'Discharged' AND "DischargedAtUtc" IS NOT NULL AND "CancelledAtUtc" IS NULL)
                """);
        });

        b.Property(x => x.Status).HasMaxLength(20).IsRequired();
        b.HasAlternateKey(x => new { x.TenantId, x.HospitalId, x.Id });
        b.HasIndex(x => new { x.TenantId, x.HospitalId, x.PatientId })
            .IsUnique()
            .HasFilter("\"Status\" = 'Active'");
        b.HasIndex(x => new { x.TenantId, x.HospitalId, x.Status, x.AdmittedAtUtc });

        b.HasOne<Hospital>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.HospitalId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne<Patient>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.PatientId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne<HospitalPatientRegistration>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.HospitalId, x.PatientId })
            .HasPrincipalKey(x => new { x.TenantId, x.HospitalId, x.PatientId })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class InpatientBedPlacementConfiguration : IEntityTypeConfiguration<InpatientBedPlacement>
{
    public void Configure(EntityTypeBuilder<InpatientBedPlacement> b)
    {
        b.ToTable("inpatient_bed_placements", table =>
            table.HasCheckConstraint(
                "CK_inpatient_bed_placements_time_range",
                "\"EndedAtUtc\" IS NULL OR \"EndedAtUtc\" > \"StartedAtUtc\""));

        b.Property(x => x.EndReason).HasMaxLength(250);

        b.HasIndex(x => new { x.TenantId, x.AdmissionId })
            .IsUnique()
            .HasFilter("\"EndedAtUtc\" IS NULL");
        b.HasIndex(x => new { x.TenantId, x.BedId })
            .IsUnique()
            .HasFilter("\"EndedAtUtc\" IS NULL");
        b.HasIndex(x => new { x.TenantId, x.HospitalId, x.StartedAtUtc });

        b.HasOne<InpatientAdmission>().WithMany(x => x.BedPlacements)
            .HasForeignKey(x => new { x.TenantId, x.HospitalId, x.AdmissionId })
            .HasPrincipalKey(x => new { x.TenantId, x.HospitalId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne<Hospital>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.HospitalId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne<Department>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.HospitalId, x.DepartmentId })
            .HasPrincipalKey(x => new { x.TenantId, x.HospitalId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne<Ward>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.DepartmentId, x.WardId })
            .HasPrincipalKey(x => new { x.TenantId, x.DepartmentId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne<Room>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.WardId, x.RoomId })
            .HasPrincipalKey(x => new { x.TenantId, x.WardId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne<Bed>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.RoomId, x.BedId })
            .HasPrincipalKey(x => new { x.TenantId, x.RoomId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
