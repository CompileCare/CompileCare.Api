using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HeapCare.Infrastructure.Persistence.Configurations;

public sealed class UserIdentityConfiguration : IEntityTypeConfiguration<UserIdentity>
{
    public void Configure(EntityTypeBuilder<UserIdentity> builder)
    {
        builder.ToTable("user_identities", table =>
            table.HasCheckConstraint(
                "CK_user_identities_identity",
                "\"Issuer\" <> '' AND \"Subject\" <> ''"));

        builder.Property(x => x.Issuer).HasMaxLength(255).IsRequired();
        builder.Property(x => x.Subject).HasMaxLength(255).IsRequired();

        // The composite principal key lets tenant memberships and platform
        // operator records prove that their legacy issuer/subject columns still
        // match the referenced global identity during the compatibility period.
        builder.HasAlternateKey(x => new { x.Id, x.Issuer, x.Subject });
        builder.HasIndex(x => new { x.Issuer, x.Subject }).IsUnique();
    }
}
