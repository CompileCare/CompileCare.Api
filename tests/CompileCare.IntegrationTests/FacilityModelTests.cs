using CompileCare.Application.Facilities;
using CompileCare.Application.Tenancy;
using CompileCare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CompileCare.IntegrationTests;

public sealed class FacilityModelTests
{
    [Theory]
    [InlineData(0, 0, 1, 50)]
    [InlineData(-5, -1, 1, 50)]
    [InlineData(2, 500, 2, 100)]
    [InlineData(3, 25, 3, 25)]
    public void PageRequest_NormalizesBounds(int page, int size, int expectedPage, int expectedSize)
    {
        var request = new PageRequest(page, size);
        Assert.Equal(expectedPage, request.SafePage);
        Assert.Equal(expectedSize, request.SafePageSize);
        Assert.Equal((expectedPage - 1) * expectedSize, request.Skip);
    }

    [Fact]
    public void PageRequest_ClampsPageToPreventSkipOverflow()
    {
        var request = new PageRequest(int.MaxValue, 100);
        Assert.True(request.Skip >= 0);
        Assert.True(request.SafePage <= int.MaxValue / PageRequest.MaxPageSize);
    }

    [Fact]
    public void PagedResult_ExposesStablePaginationMetadata()
    {
        var result = new PagedResult<string>(["one", "two"], 2, 2, 5);
        Assert.Equal(3, result.TotalPages);
        Assert.Equal(2, result.Page);
        Assert.Equal(5, result.TotalCount);
    }

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
            Assert.True(entityType.FindProperty(nameof(TenantOwnedEntity.Version))!.IsConcurrencyToken);
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
