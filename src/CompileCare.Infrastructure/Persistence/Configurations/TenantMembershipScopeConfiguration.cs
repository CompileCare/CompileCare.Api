using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CompileCare.Infrastructure.Persistence.Configurations;

public sealed class TenantMembershipScopeConfiguration : IEntityTypeConfiguration<TenantMembershipScope>
{
    public void Configure(EntityTypeBuilder<TenantMembershipScope> builder)
    {
        builder.ToTable("tenant_membership_scopes", table =>
            table.HasCheckConstraint(
                "CK_tenant_membership_scopes_shape",
                """
                ("ScopeType" = 'Tenant' AND "HospitalId" IS NULL AND "DepartmentId" IS NULL)
                OR ("ScopeType" = 'Hospital' AND "HospitalId" IS NOT NULL AND "DepartmentId" IS NULL)
                OR ("ScopeType" = 'Department' AND "HospitalId" IS NOT NULL AND "DepartmentId" IS NOT NULL)
                """));

        builder.Property(x => x.ScopeType).HasMaxLength(20).IsRequired();
        builder.HasOne(x => x.Membership).WithMany(x => x.Scopes)
            .HasForeignKey(x => new { x.TenantId, x.MembershipId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Tenant>().WithMany()
            .HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Hospital>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.HospitalId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Department>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.HospitalId, x.DepartmentId })
            .HasPrincipalKey(x => new { x.TenantId, x.HospitalId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.TenantId, x.MembershipId })
            .IsUnique().HasFilter("\"ScopeType\" = 'Tenant'");
        builder.HasIndex(x => new { x.TenantId, x.MembershipId, x.HospitalId })
            .IsUnique().HasFilter("\"ScopeType\" = 'Hospital'");
        builder.HasIndex(x => new { x.TenantId, x.MembershipId, x.DepartmentId })
            .IsUnique().HasFilter("\"ScopeType\" = 'Department'");
    }
}
