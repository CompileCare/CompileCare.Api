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
            var currentTenant = Expression.Property(Expression.Constant(context), nameof(context.CurrentTenantId));
            var predicate = Expression.Equal(
                tenantProperty,
                Expression.Property(currentTenant, nameof(Nullable<Guid>.Value)));
            var hasTenant = Expression.Property(currentTenant, nameof(Nullable<Guid>.HasValue));
            var filter = Expression.Lambda(Expression.AndAlso(hasTenant, predicate), parameter);
            modelBuilder.Entity(entityType.ClrType).HasQueryFilter(filter);
        }
    }
}