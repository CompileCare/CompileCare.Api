using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CompileCare.Infrastructure.Persistence.Configurations;

public sealed class PatientConfiguration : IEntityTypeConfiguration<Patient>
{
    public void Configure(EntityTypeBuilder<Patient> builder)
    {
        builder.ToTable("patients");
        builder.Property(x => x.GivenName).HasMaxLength(100).IsRequired();
        builder.Property(x => x.MiddleName).HasMaxLength(100);
        builder.Property(x => x.FamilyName).HasMaxLength(100);
        builder.HasAlternateKey(x => new { x.TenantId, x.Id });
        builder.HasIndex(x => new { x.TenantId, x.FamilyName, x.GivenName });
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
        builder.Property(x => x.MedicalRecordNumber).HasMaxLength(64);
        builder.HasAlternateKey(x => new { x.TenantId, x.Id });
        builder.HasIndex(x => new { x.TenantId, x.HospitalId, x.PatientId }).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.HospitalId, x.MedicalRecordNumber })
            .IsUnique().HasFilter("\"MedicalRecordNumber\" IS NOT NULL");
        builder.HasOne(x => x.Hospital).WithMany()
            .HasForeignKey(x => new { x.TenantId, x.HospitalId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
