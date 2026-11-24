using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CompileCare.Infrastructure.Persistence.Configurations;

public sealed class StaffProfileConfiguration : IEntityTypeConfiguration<StaffProfile>
{
    public void Configure(EntityTypeBuilder<StaffProfile> builder)
    {
        builder.ToTable("staff_profiles");
        builder.Property(x => x.StaffCode).HasMaxLength(40).IsRequired();
        builder.Property(x => x.GivenName).HasMaxLength(100).IsRequired();
        builder.Property(x => x.MiddleName).HasMaxLength(100);
        builder.Property(x => x.FamilyName).HasMaxLength(100);
        builder.Property(x => x.ProviderDesignation).HasMaxLength(100);
        builder.HasAlternateKey(x => new { x.TenantId, x.Id });
        builder.HasIndex(x => new { x.TenantId, x.StaffCode }).IsUnique();
    }
}

public sealed class StaffAssignmentConfiguration : IEntityTypeConfiguration<StaffAssignment>
{
    public void Configure(EntityTypeBuilder<StaffAssignment> builder)
    {
        builder.ToTable("staff_assignments", table =>
            table.HasCheckConstraint(
                "CK_staff_assignments_effective_range",
                "\"EffectiveTo\" IS NULL OR \"EffectiveTo\" >= \"EffectiveFrom\""));
        builder.HasAlternateKey(x => new { x.TenantId, x.Id });
        builder.HasIndex(x => new { x.TenantId, x.HospitalId });
        builder.HasIndex(x => new { x.TenantId, x.HospitalId, x.DepartmentId });
        builder.HasIndex(x => new { x.TenantId, x.StaffProfileId, x.HospitalId })
            .IsUnique()
            .HasFilter("\"DepartmentId\" IS NULL AND \"IsActive\" = TRUE AND \"EffectiveTo\" IS NULL");
        builder.HasIndex(x => new { x.TenantId, x.StaffProfileId, x.HospitalId, x.DepartmentId })
            .IsUnique()
            .HasFilter("\"DepartmentId\" IS NOT NULL AND \"IsActive\" = TRUE AND \"EffectiveTo\" IS NULL");
        builder.HasOne<StaffProfile>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.StaffProfileId })
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
