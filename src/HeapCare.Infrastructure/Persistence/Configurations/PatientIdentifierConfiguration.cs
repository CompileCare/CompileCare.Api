using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HeapCare.Infrastructure.Persistence.Configurations;

public sealed class PatientIdentifierConfiguration : IEntityTypeConfiguration<PatientIdentifier>
{
    public void Configure(EntityTypeBuilder<PatientIdentifier> builder)
    {
        builder.ToTable("patient_identifiers", table =>
        {
            table.HasCheckConstraint(
                "CK_patient_identifiers_type_code",
                "\"TypeCode\" = 'MRN'");
            table.HasCheckConstraint(
                "CK_patient_identifiers_normalized_value",
                "\"NormalizedValue\" <> '' AND \"NormalizedValue\" = upper(btrim(\"NormalizedValue\"))");
        });

        builder.Property(x => x.TypeCode).HasMaxLength(32).IsRequired();
        builder.Property(x => x.Value).HasMaxLength(40).IsRequired();
        builder.Property(x => x.NormalizedValue).HasMaxLength(40).IsRequired();
        builder.HasAlternateKey(x => new { x.TenantId, x.Id });
        builder.HasIndex(x => new { x.TenantId, x.HospitalId, x.TypeCode, x.NormalizedValue })
            .IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.HospitalId, x.PatientId, x.TypeCode })
            .IsUnique();
        builder.HasOne(x => x.Registration).WithMany()
            .HasForeignKey(x => new { x.TenantId, x.HospitalId, x.PatientId })
            .HasPrincipalKey(x => new { x.TenantId, x.HospitalId, x.PatientId })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class HospitalPatientIdentifierPolicyConfiguration : IEntityTypeConfiguration<HospitalPatientIdentifierPolicy>
{
    public void Configure(EntityTypeBuilder<HospitalPatientIdentifierPolicy> builder)
    {
        builder.ToTable("hospital_patient_identifier_policies", table =>
        {
            table.HasCheckConstraint(
                "CK_hospital_patient_identifier_policies_number_length",
                "\"NumberLength\" BETWEEN 1 AND 12");
            table.HasCheckConstraint(
                "CK_hospital_patient_identifier_policies_next_number",
                "\"NextNumber\" > 0");
            table.HasCheckConstraint(
                "CK_hospital_patient_identifier_policies_prefix",
                "\"Prefix\" ~ '^[A-Z0-9][A-Z0-9-]{0,19}$'");
        });

        builder.Property(x => x.Prefix).HasMaxLength(20).IsRequired();
        builder.Property(x => x.NumberLength).IsRequired();
        builder.Property(x => x.NextNumber).IsRequired();
        builder.HasAlternateKey(x => new { x.TenantId, x.Id });
        builder.HasIndex(x => new { x.TenantId, x.HospitalId }).IsUnique();
        builder.HasOne(x => x.Hospital).WithMany()
            .HasForeignKey(x => new { x.TenantId, x.HospitalId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
