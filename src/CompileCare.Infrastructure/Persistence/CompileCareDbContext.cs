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
    public DbSet<TenantMembership> TenantMemberships => Set<TenantMembership>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CompileCareDbContext).Assembly);

        // Version is updated on each write and must participate in the UPDATE/DELETE
        // predicate so stale writes fail rather than silently overwriting newer data.
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (typeof(TenantOwnedEntity).IsAssignableFrom(entityType.ClrType) && !entityType.ClrType.IsAbstract)
                modelBuilder.Entity(entityType.ClrType).Property(nameof(TenantOwnedEntity.Version)).IsConcurrencyToken();
        }

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
