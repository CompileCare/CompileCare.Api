using CompileCare.Application.Tenancy;
using CompileCare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace CompileCare.IntegrationTests;

public sealed class FacilityModelTests
{
    private static CompileCareDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<CompileCareDbContext>()
            .UseNpgsql("Host=localhost;Database=compilecare;Username=test;Password=test")
            .Options;
        return new CompileCareDbContext(options, new TenantContext());
    }

    [Fact]
    public void TenantOwnedEntities_UseVersionAsConcurrencyToken()
    {
        using var db = CreateContext();
        foreach (var entityType in db.Model.GetEntityTypes().Where(t => typeof(TenantOwnedEntity).IsAssignableFrom(t.ClrType) && !t.ClrType.IsAbstract))
        {
            Assert.True(entityType.FindProperty(nameof(TenantOwnedEntity.Version))!.IsConcurrencyToken);
        }
    }

    [Fact]
    public void HospitalNameAndCode_AreUniqueWithinTenant()
    {
        using var db = CreateContext();
        var hospital = db.Model.FindEntityType(typeof(Hospital))!;
        Assert.Contains(hospital.GetIndexes(), index =>
            index.IsUnique && index.Properties.Select(p => p.Name).SequenceEqual([nameof(Hospital.TenantId), nameof(Hospital.Name)]));
        Assert.Contains(hospital.GetIndexes(), index =>
            index.IsUnique && index.Properties.Select(p => p.Name).SequenceEqual([nameof(Hospital.TenantId), nameof(Hospital.Code)]));
    }

    [Fact]
    public void HierarchyRelationships_UseTenantScopedForeignKeysAndRestrictiveDelete()
    {
        using var db = CreateContext();
        AssertTenantScopedRestrictiveRelationship<Department, Hospital>(db, nameof(Department.HospitalId));
        AssertTenantScopedRestrictiveRelationship<Ward, Department>(db, nameof(Ward.DepartmentId));
        AssertTenantScopedRestrictiveRelationship<Room, Ward>(db, nameof(Room.WardId));
        AssertTenantScopedRestrictiveRelationship<Bed, Room>(db, nameof(Bed.RoomId));
    }

    private static void AssertTenantScopedRestrictiveRelationship<TChild, TParent>(CompileCareDbContext db, string parentIdProperty)
        where TChild : TenantOwnedEntity
        where TParent : TenantOwnedEntity
    {
        var child = db.Model.FindEntityType(typeof(TChild))!;
        var parent = db.Model.FindEntityType(typeof(TParent))!;
        var foreignKey = child.GetForeignKeys().Single(fk => fk.PrincipalEntityType == parent);
        Assert.Equal(DeleteBehavior.Restrict, foreignKey.DeleteBehavior);
        Assert.Equal(new[] { nameof(TenantOwnedEntity.TenantId), parentIdProperty }, foreignKey.Properties.Select(p => p.Name));
        Assert.Equal(new[] { nameof(TenantOwnedEntity.TenantId), nameof(TenantOwnedEntity.Id) }, foreignKey.PrincipalKey.Properties.Select(p => p.Name));
    }
}
