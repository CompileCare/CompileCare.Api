using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HeapCare.Infrastructure.Persistence.Configurations;

public sealed class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.ToTable("tenants", table =>
        {
            table.HasCheckConstraint("CK_tenants_slug_format", "\"Slug\" ~ '^[a-z0-9]+(-[a-z0-9]+)*$'");
            table.HasCheckConstraint("CK_tenants_status", "\"Status\" IN ('Provisioning', 'Active', 'Suspended', 'Closed')");
        });
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Slug).HasMaxLength(63).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(20).IsRequired();
        builder.HasIndex(x => x.Slug).IsUnique();
    }
}

public sealed class PlatformOperatorConfiguration : IEntityTypeConfiguration<PlatformOperator>
{
    public void Configure(EntityTypeBuilder<PlatformOperator> builder)
    {
        builder.ToTable("platform_operators", table =>
            table.HasCheckConstraint(
                "CK_platform_operators_identity",
                "\"Issuer\" <> '' AND \"Subject\" <> ''"));
        builder.Property(x => x.UserIdentityId).IsRequired();
        builder.Property(x => x.Issuer).HasMaxLength(255).IsRequired();
        builder.Property(x => x.Subject).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => new { x.Issuer, x.Subject }).IsUnique();
        builder.HasOne<UserIdentity>().WithMany()
            .HasForeignKey(x => new { x.UserIdentityId, x.Issuer, x.Subject })
            .HasPrincipalKey(x => new { x.Id, x.Issuer, x.Subject })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
