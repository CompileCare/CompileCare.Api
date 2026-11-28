using HeapCare.Application.Tenancy;
using HeapCare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HeapCare.IntegrationTests;

[Collection("PostgreSQL integration")]
public sealed class DatabaseMigrationTests(FacilityApiFactory factory)
{
    [Fact]
    public async Task CleanDatabase_IsBuiltFromVersionedMigrations()
    {
        factory.ResetDatabase();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HeapCareDbContext>();

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
    public async Task TenantRegistryMigration_BackfillsExistingTenantMemberships()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HeapCareDbContext>();
        var databaseName = db.Database.GetDbConnection().Database;

        if (!databaseName.EndsWith("_test", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Refusing to reset database '{databaseName}'. Integration tests require a dedicated database ending in '_test'.");
        }

        await db.Database.EnsureDeletedAsync();

        var migrator = db.Database.GetService<IMigrator>();
        await migrator.MigrateAsync("20261123163601_AddAuditEvents");

        var tenantId = Guid.NewGuid();
        var membershipId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO "tenant_memberships"
                ("Id", "Issuer", "Subject", "Role", "IsActive", "TenantId",
                 "CreatedAtUtc", "UpdatedAtUtc", "Version")
            VALUES
                ({membershipId}, {TestJwtTokenFactory.Issuer}, {TestJwtTokenFactory.Subject},
                 {TenantRoles.HospitalAdmin}, TRUE, {tenantId}, {now}, {now}, {Guid.NewGuid()});
            """);

        await migrator.MigrateAsync();

        var tenant = await db.Tenants.SingleAsync(value => value.Id == tenantId);
        Assert.Equal("Legacy tenant", tenant.Name);
        Assert.Equal($"legacy-{tenantId:N}", tenant.Slug);
        Assert.Equal(TenantStatuses.Active, tenant.Status);

        var membership = await db.TenantMemberships
            .IgnoreQueryFilters()
            .SingleAsync(value => value.Id == membershipId);
        Assert.Equal(tenantId, membership.TenantId);
        Assert.Equal(TestJwtTokenFactory.Issuer, membership.Issuer);
        Assert.Equal(TestJwtTokenFactory.Subject, membership.Subject);
        Assert.NotEqual(Guid.Empty, membership.UserIdentityId);
        var identity = await db.UserIdentities.SingleAsync(
            value => value.Id == membership.UserIdentityId);
        Assert.Equal(membership.Issuer, identity.Issuer);
        Assert.Equal(membership.Subject, identity.Subject);
    }

    [Fact]
    public async Task TenantMembershipScopeMigration_BackfillsLegacyMembershipsAsTenantWide()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HeapCareDbContext>();
        var databaseName = db.Database.GetDbConnection().Database;

        if (!databaseName.EndsWith("_test", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Refusing to reset database '{databaseName}'. Integration tests require a dedicated database ending in '_test'.");
        }

        await db.Database.EnsureDeletedAsync();
        var migrator = db.Database.GetService<IMigrator>();
        await migrator.MigrateAsync("20261124100000_AddTenantRegistry");

        var tenantId = Guid.NewGuid();
        var membershipId = Guid.NewGuid();
        db.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = "Scope backfill tenant",
            Slug = $"scope-backfill-{tenantId:N}",
            Status = TenantStatuses.Active
        });
        await db.SaveChangesAsync();

        var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        tenantContext.SetTenant(tenantId);

        // Insert through the pre-scope/pre-identity schema. The current EF model
        // intentionally contains the later UserIdentityId column, which does not
        // exist until the final migration in this rehearsal.
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO "tenant_memberships"
                ("Id", "Issuer", "Subject", "Role", "IsActive", "TenantId",
                 "CreatedAtUtc", "UpdatedAtUtc", "Version")
            VALUES
                ({membershipId}, {TestJwtTokenFactory.Issuer}, {TestJwtTokenFactory.Subject},
                 {TenantRoles.FacilityManager}, TRUE, {tenantId},
                 {DateTimeOffset.UtcNow}, {DateTimeOffset.UtcNow}, {Guid.NewGuid()});
            """);

        var platformOperatorId = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO "platform_operators"
                ("Id", "Issuer", "Subject", "IsActive",
                 "CreatedAtUtc", "UpdatedAtUtc", "Version")
            VALUES
                ({platformOperatorId}, {TestJwtTokenFactory.Issuer}, {TestJwtTokenFactory.Subject},
                 TRUE, {DateTimeOffset.UtcNow}, {DateTimeOffset.UtcNow}, {Guid.NewGuid()});
            """);

        await migrator.MigrateAsync();

        var scopeRecord = await db.TenantMembershipScopes
            .IgnoreQueryFilters()
            .SingleAsync(value => value.MembershipId == membershipId);

        Assert.Equal(tenantId, scopeRecord.TenantId);
        Assert.Equal(OrganizationScopeTypes.Tenant, scopeRecord.ScopeType);
        Assert.Null(scopeRecord.HospitalId);
        Assert.Null(scopeRecord.DepartmentId);

        var migratedMembership = await db.TenantMemberships.IgnoreQueryFilters()
            .SingleAsync(value => value.Id == membershipId);
        var migratedOperator = await db.PlatformOperators.SingleAsync(
            value => value.Id == platformOperatorId);
        Assert.NotEqual(Guid.Empty, migratedMembership.UserIdentityId);
        Assert.Equal(migratedMembership.UserIdentityId, migratedOperator.UserIdentityId);
        Assert.Equal(
            1,
            await db.UserIdentities.CountAsync(value =>
                value.Issuer == TestJwtTokenFactory.Issuer &&
                value.Subject == TestJwtTokenFactory.Subject));
    }

    [Fact]
    public async Task ApplyingMigrationsMoreThanOnce_IsIdempotent()
    {
        factory.ResetDatabase();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HeapCareDbContext>();

        var appliedBefore = (await db.Database.GetAppliedMigrationsAsync()).ToArray();

        await db.Database.MigrateAsync();

        var appliedAfter = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
        Assert.Equal(appliedBefore, appliedAfter);
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
    }
}
