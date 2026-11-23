using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

namespace CompileCare.Infrastructure.Persistence;

internal static class TenantModelConfiguration
{
    public static void ApplyTenantFilters(ModelBuilder modelBuilder, CompileCareDbContext context)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (entityType.IsOwned() || entityType.ClrType.IsAbstract ||
                !typeof(TenantOwnedEntity).IsAssignableFrom(entityType.ClrType))
                continue;

            var parameter = Expression.Parameter(entityType.ClrType, "entity");
            var tenantProperty = Expression.Property(parameter, nameof(TenantOwnedEntity.TenantId));
            var nullableEntityTenant = Expression.Convert(tenantProperty, typeof(Guid?));
            var currentTenant = Expression.Property(
                Expression.Constant(context),
                nameof(context.CurrentTenantId));

            // Comparing nullable values avoids eagerly evaluating Nullable<Guid>.Value
            // when no tenant is set. A null context therefore matches no tenant-owned rows.
            var filter = Expression.Lambda(
                Expression.Equal(nullableEntityTenant, currentTenant),
                parameter);

            modelBuilder.Entity(entityType.ClrType).HasQueryFilter(filter);
        }
    }
}
