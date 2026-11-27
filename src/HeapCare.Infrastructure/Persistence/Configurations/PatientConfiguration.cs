using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HeapCare.Infrastructure.Persistence.Configurations;

public sealed class PatientConfiguration : IEntityTypeConfiguration<Patient>
{
    public void Configure(EntityTypeBuilder<Patient> builder)
    {
        builder.ToTable("patients", table =>
            table.HasCheckConstraint("CK_patients_normalized_given_name", "\"NormalizedGivenName\" <> ''"));
        builder.Property(x => x.GivenName).HasMaxLength(100).IsRequired();
        builder.Property(x => x.MiddleName).HasMaxLength(100);
        builder.Property(x => x.FamilyName).HasMaxLength(100);
        builder.Property(x => x.NormalizedGivenName).HasMaxLength(100).IsRequired();
        builder.Property(x => x.NormalizedMiddleName).HasMaxLength(100);
        builder.Property(x => x.NormalizedFamilyName).HasMaxLength(100);
        builder.HasAlternateKey(x => new { x.TenantId, x.Id });
        builder.HasIndex(x => new { x.TenantId, x.FamilyName, x.GivenName });
        builder.HasIndex(x => new { x.TenantId, x.DateOfBirth, x.NormalizedGivenName });
        builder.HasIndex(x => new
        {
            x.TenantId,
            x.DateOfBirth,
            x.NormalizedMiddleName,
            x.NormalizedFamilyName
        });
        builder.HasMany(x => x.HospitalRegistrations).WithOne(x => x.Patient)
            .HasForeignKey(x => new { x.TenantId, x.PatientId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class HospitalPatientRegistrationConfiguration : IEntityTypeConfiguration<HospitalPatientRegistration>
{
    public void Configure(EntityTypeBuilder<HospitalPatientRegistration> builder)
    {
        builder.ToTable("hospital_patient_registrations");
        builder.HasAlternateKey(x => new { x.TenantId, x.Id });
        builder.HasAlternateKey(x => new { x.TenantId, x.HospitalId, x.PatientId });
        builder.HasOne(x => x.Hospital).WithMany()
            .HasForeignKey(x => new { x.TenantId, x.HospitalId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
