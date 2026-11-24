using CompileCare.Application.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace CompileCare.Infrastructure.Persistence;

public sealed class CompileCareDbContext(
    DbContextOptions<CompileCareDbContext> options,
    ITenantContext tenantContext) : DbContext(options)
{
    public Guid? CurrentTenantId => tenantContext.TenantId;
    public DbSet<Hospital> Hospitals => Set<Hospital>();
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<Ward> Wards => Set<Ward>();
    public DbSet<Room> Rooms => Set<Room>();
    public DbSet<Bed> Beds => Set<Bed>();
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<PlatformOperator> PlatformOperators => Set<PlatformOperator>();
    public DbSet<TenantMembership> TenantMemberships => Set<TenantMembership>();
    public DbSet<TenantMembershipScope> TenantMembershipScopes => Set<TenantMembershipScope>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CompileCareDbContext).Assembly);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (typeof(TenantOwnedEntity).IsAssignableFrom(entityType.ClrType) && !entityType.ClrType.IsAbstract)
                modelBuilder.Entity(entityType.ClrType).Property(nameof(TenantOwnedEntity.Version)).IsConcurrencyToken();
            if (typeof(IPlatformEntity).IsAssignableFrom(entityType.ClrType))
                modelBuilder.Entity(entityType.ClrType).Property(nameof(IPlatformEntity.Version)).IsConcurrencyToken();
        }

        modelBuilder.Entity<Hospital>().HasOne<Tenant>().WithMany()
            .HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Department>().HasOne<Tenant>().WithMany()
            .HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Ward>().HasOne<Tenant>().WithMany()
            .HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Room>().HasOne<Tenant>().WithMany()
            .HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Bed>().HasOne<Tenant>().WithMany()
            .HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<TenantMembership>().HasOne<Tenant>().WithMany()
            .HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<TenantMembershipScope>().HasOne<Tenant>().WithMany()
            .HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<AuditEvent>().HasOne<Tenant>().WithMany()
            .HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);

        TenantModelConfiguration.ApplyTenantFilters(modelBuilder, this);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ApplyAuditAndTenantRules();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        ApplyAuditAndTenantRules();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void ApplyAuditAndTenantRules()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var entry in ChangeTracker.Entries<AuditEvent>())
        {
            if (entry.State == EntityState.Added)
            {
                if (entry.Entity.TenantId.HasValue &&
                    (CurrentTenantId is null || entry.Entity.TenantId != CurrentTenantId))
                    throw new InvalidOperationException("An audit event cannot claim an unvalidated tenant context.");
                entry.Entity.TenantId = CurrentTenantId;
                if (entry.Entity.Id == Guid.Empty ||
                    string.IsNullOrWhiteSpace(entry.Entity.Action) ||
                    string.IsNullOrWhiteSpace(entry.Entity.ResourceType) ||
                    string.IsNullOrWhiteSpace(entry.Entity.Outcome) ||
                    string.IsNullOrWhiteSpace(entry.Entity.CorrelationId))
                    throw new InvalidOperationException("An audit event requires its identifier, action, resource type, outcome and correlation ID.");
                if (entry.Entity.OccurredAtUtc == default)
                    entry.Entity.OccurredAtUtc = now;
            }
            else if (entry.State is EntityState.Modified or EntityState.Deleted)
                throw new InvalidOperationException("Audit events are append-only and cannot be modified or deleted.");
        }

        foreach (var entry in ChangeTracker.Entries<IPlatformEntity>())
        {
            if (entry.State == EntityState.Added)
            {
                if (entry.Entity.Id == Guid.Empty)
                    throw new InvalidOperationException("A platform entity requires a non-empty identifier.");
                entry.Entity.CreatedAtUtc = now;
                entry.Entity.UpdatedAtUtc = now;
                entry.Entity.Version = Guid.NewGuid();
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Property(x => x.Id).IsModified = false;
                entry.Property(x => x.CreatedAtUtc).IsModified = false;
                entry.Entity.UpdatedAtUtc = now;
                entry.Entity.Version = Guid.NewGuid();
            }
            else if (entry.State == EntityState.Deleted)
                throw new InvalidOperationException("Platform records cannot be physically deleted.");
        }

        foreach (var entry in ChangeTracker.Entries<TenantOwnedEntity>())
        {
            if (entry.State is EntityState.Added)
            {
                if (CurrentTenantId is null || CurrentTenantId == Guid.Empty)
                    throw new InvalidOperationException("A validated tenant context is required to persist tenant-owned data.");
                if (entry.Entity.TenantId != Guid.Empty && entry.Entity.TenantId != CurrentTenantId)
                    throw new InvalidOperationException("The entity tenant does not match the current tenant context.");
                entry.Entity.TenantId = CurrentTenantId.Value;
                entry.Entity.CreatedAtUtc = now;
                entry.Entity.UpdatedAtUtc = now;
                entry.Entity.Version = Guid.NewGuid();
            }
            else if (entry.State is EntityState.Modified)
            {
                if (CurrentTenantId is null || entry.Entity.TenantId != CurrentTenantId)
                    throw new InvalidOperationException("Tenant context mismatch while updating tenant-owned data.");
                entry.Property(x => x.TenantId).IsModified = false;
                entry.Property(x => x.CreatedAtUtc).IsModified = false;
                entry.Entity.UpdatedAtUtc = now;
                entry.Entity.Version = Guid.NewGuid();
            }
            else if (entry.State is EntityState.Deleted)
            {
                if (CurrentTenantId is null || entry.Entity.TenantId != CurrentTenantId)
                    throw new InvalidOperationException("Tenant context mismatch while deleting tenant-owned data.");
            }
        }
    }
}
