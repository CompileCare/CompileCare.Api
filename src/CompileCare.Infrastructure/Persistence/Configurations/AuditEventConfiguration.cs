using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CompileCare.Infrastructure.Persistence.Configurations;

public sealed class AuditEventConfiguration : IEntityTypeConfiguration<AuditEvent>
{
    public void Configure(EntityTypeBuilder<AuditEvent> builder)
    {
        builder.ToTable("audit_events", table =>
            table.HasCheckConstraint(
                "CK_audit_events_outcome",
                "\"\"Outcome\"\" IN ('Succeeded', 'Denied', 'Rejected', 'Failed')"));

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.ActorIssuer).HasMaxLength(255);
        builder.Property(x => x.ActorSubject).HasMaxLength(255);
        builder.Property(x => x.Action).HasMaxLength(128).IsRequired();
        builder.Property(x => x.ResourceType).HasMaxLength(128).IsRequired();
        builder.Property(x => x.HttpMethod).HasMaxLength(10).IsRequired();
        builder.Property(x => x.RouteTemplate).HasMaxLength(500).IsRequired();
        builder.Property(x => x.Outcome).HasMaxLength(16).IsRequired();
        builder.Property(x => x.CorrelationId).HasMaxLength(64).IsRequired();
        builder.Property(x => x.StatusCode).IsRequired();
        builder.Property(x => x.OccurredAtUtc).IsRequired();

        builder.HasIndex(x => new { x.TenantId, x.OccurredAtUtc });
        builder.HasIndex(x => new { x.ResourceType, x.ResourceId, x.OccurredAtUtc });
    }
}
