using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CompileCare.Infrastructure.Persistence.Configurations;

public sealed class OpdEncounterConfiguration : IEntityTypeConfiguration<OpdEncounter>
{
    public void Configure(EntityTypeBuilder<OpdEncounter> builder)
    {
        builder.ToTable("opd_encounters", table =>
        {
            table.HasCheckConstraint("CK_opd_encounters_status",
                "\"Status\" IN ('InProgress', 'Signed', 'Closed', 'Cancelled')");
            table.HasCheckConstraint("CK_opd_encounters_signed_attribution",
                "\"Status\" NOT IN ('Signed', 'Closed') OR (\"SignedAtUtc\" IS NOT NULL AND \"SignedByIssuer\" IS NOT NULL AND \"SignedBySubject\" IS NOT NULL)");
            table.HasCheckConstraint("CK_opd_encounters_cancel_reason",
                "\"Status\" <> 'Cancelled' OR (\"CancellationReason\" IS NOT NULL AND length(trim(\"CancellationReason\")) BETWEEN 1 AND 250)");
        });

        builder.Property(x => x.Status).HasMaxLength(20).IsRequired();
        builder.Property(x => x.ChiefComplaint).HasMaxLength(500);
        builder.Property(x => x.History).HasMaxLength(4000);
        builder.Property(x => x.Examination).HasMaxLength(4000);
        builder.Property(x => x.Assessment).HasMaxLength(2000);
        builder.Property(x => x.Plan).HasMaxLength(4000);
        builder.Property(x => x.CreatedByIssuer).HasMaxLength(255).IsRequired();
        builder.Property(x => x.CreatedBySubject).HasMaxLength(255).IsRequired();
        builder.Property(x => x.SignedByIssuer).HasMaxLength(255);
        builder.Property(x => x.SignedBySubject).HasMaxLength(255);
        builder.Property(x => x.CancellationReason).HasMaxLength(250);

        builder.HasAlternateKey(x => new { x.TenantId, x.Id });
        builder.HasIndex(x => new { x.TenantId, x.QueueEntryId }).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.HospitalId, x.PatientId, x.StartedAtUtc });

        builder.HasOne<QueueEntry>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.QueueEntryId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Hospital>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.HospitalId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Department>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.HospitalId, x.DepartmentId })
            .HasPrincipalKey(x => new { x.TenantId, x.HospitalId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<HospitalPatientRegistration>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.HospitalId, x.PatientId })
            .HasPrincipalKey(x => new { x.TenantId, x.HospitalId, x.PatientId })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class OpdEncounterAmendmentConfiguration : IEntityTypeConfiguration<OpdEncounterAmendment>
{
    public void Configure(EntityTypeBuilder<OpdEncounterAmendment> builder)
    {
        builder.ToTable("opd_encounter_amendments", table =>
        {
            table.HasCheckConstraint("CK_opd_encounter_amendments_reason",
                "length(trim(\"Reason\")) BETWEEN 1 AND 250");
            table.HasCheckConstraint("CK_opd_encounter_amendments_content",
                "length(trim(\"Content\")) BETWEEN 1 AND 4000");
        });

        builder.Property(x => x.Reason).HasMaxLength(250).IsRequired();
        builder.Property(x => x.Content).HasMaxLength(4000).IsRequired();
        builder.Property(x => x.AuthorIssuer).HasMaxLength(255).IsRequired();
        builder.Property(x => x.AuthorSubject).HasMaxLength(255).IsRequired();

        builder.HasAlternateKey(x => new { x.TenantId, x.Id });
        builder.HasIndex(x => new { x.TenantId, x.EncounterId, x.CreatedAtUtc });

        builder.HasOne<OpdEncounter>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.EncounterId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Hospital>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.HospitalId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Department>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.HospitalId, x.DepartmentId })
            .HasPrincipalKey(x => new { x.TenantId, x.HospitalId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
