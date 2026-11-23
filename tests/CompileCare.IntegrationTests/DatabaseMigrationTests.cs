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

        // These checks ensure the versioned schema is queryable, not just recorded
        // in __EFMigrationsHistory.
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

        await db.Database.MigrateAsync();

        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.Single(await db.Database.GetAppliedMigrationsAsync());
    }
}
