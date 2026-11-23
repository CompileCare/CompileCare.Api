using CompileCare.Application.Tenancy;
using CompileCare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CompileCare.IntegrationTests;

[Collection("PostgreSQL integration")]
public sealed class DatabaseMigrationTests(FacilityApiFactory factory)
{
    [Fact]
    public async Task CleanDatabase_IsBuiltFromVersionedMigrations()
    {
        factory.ResetDatabase();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompileCareDbContext>();

        var appliedMigrations = await db.Database.GetAppliedMigrationsAsync();
        var pendingMigrations = await db.Database.GetPendingMigrationsAsync();

        Assert.Contains(appliedMigrations, migration =>
            migration.EndsWith("_InitialFacilityHierarchy", StringComparison.Ordinal));
        Assert.Empty(pendingMigrations);

        // A missing tenant context must safely expose no tenant-owned data.
        // It must not evaluate Nullable<Guid>.Value or throw.
        Assert.Equal(0, await db.Hospitals.CountAsync());

        var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        tenantContext.SetTenant(Guid.NewGuid());

        // These checks ensure the versioned schema is queryable for a tenant,
        // not just recorded in __EFMigrationsHistory.
        Assert.Equal(0, await db.Hospitals.CountAsync());
        Assert.Equal(0, await db.Departments.CountAsync());
        Assert.Equal(0, await db.Wards.CountAsync());
        Assert.Equal(0, await db.Rooms.CountAsync());
        Assert.Equal(0, await db.Beds.CountAsync());
    }

    [Fact]
    public async Task ApplyingMigrationsMoreThanOnce_IsIdempotent()
    {
        factory.ResetDatabase();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompileCareDbContext>();

        var appliedBefore = (await db.Database.GetAppliedMigrationsAsync()).ToArray();

        await db.Database.MigrateAsync();

        var appliedAfter = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
        Assert.Equal(appliedBefore, appliedAfter);
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
    }
}
