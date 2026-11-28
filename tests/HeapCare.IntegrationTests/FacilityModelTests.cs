using HeapCare.Application.Facilities;
using HeapCare.Application.Tenancy;
using HeapCare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HeapCare.IntegrationTests;

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

    [Fact]
    public void InpatientModel_EnforcesTenantHospitalRegistrationAndSingleActiveBedPlacement()
    {
        using var db = CreateContext();

        var admission = db.Model.FindEntityType(typeof(InpatientAdmission))!;
        Assert.Contains(admission.GetKeys(), key =>
            key.Properties.Select(property => property.Name)
                .SequenceEqual([
                    nameof(TenantOwnedEntity.TenantId),
                    nameof(InpatientAdmission.HospitalId),
                    nameof(InpatientAdmission.Id)
                ]));

        var activeAdmission = Assert.Single(admission.GetIndexes(), index => index.IsUnique);
        Assert.Equal(
            new[] { nameof(TenantOwnedEntity.TenantId), nameof(InpatientAdmission.HospitalId), nameof(InpatientAdmission.PatientId) },
            activeAdmission.Properties.Select(property => property.Name));
        Assert.Equal("\"Status\" = 'Active'", activeAdmission.GetFilter());

        AssertCompositeRestrictiveRelationship<InpatientAdmission, Hospital>(
            db,
            [nameof(TenantOwnedEntity.TenantId), nameof(InpatientAdmission.HospitalId)],
            [nameof(TenantOwnedEntity.TenantId), nameof(TenantOwnedEntity.Id)]);

        AssertCompositeRestrictiveRelationship<InpatientAdmission, Patient>(
            db,
            [nameof(TenantOwnedEntity.TenantId), nameof(InpatientAdmission.PatientId)],
            [nameof(TenantOwnedEntity.TenantId), nameof(TenantOwnedEntity.Id)]);

        AssertCompositeRestrictiveRelationship<InpatientAdmission, HospitalPatientRegistration>(
            db,
            [nameof(TenantOwnedEntity.TenantId), nameof(InpatientAdmission.HospitalId), nameof(InpatientAdmission.PatientId)],
            [nameof(TenantOwnedEntity.TenantId), nameof(HospitalPatientRegistration.HospitalId), nameof(HospitalPatientRegistration.PatientId)]);

        var placement = db.Model.FindEntityType(typeof(InpatientBedPlacement))!;
        var activeAdmissionPlacement = Assert.Single(placement.GetIndexes(), index =>
            index.IsUnique && index.Properties.Select(property => property.Name)
                .SequenceEqual([nameof(TenantOwnedEntity.TenantId), nameof(InpatientBedPlacement.AdmissionId)]));
        Assert.Equal("\"EndedAtUtc\" IS NULL", activeAdmissionPlacement.GetFilter());

        var activeBedOccupancy = Assert.Single(placement.GetIndexes(), index =>
            index.IsUnique && index.Properties.Select(property => property.Name)
                .SequenceEqual([nameof(TenantOwnedEntity.TenantId), nameof(InpatientBedPlacement.BedId)]));
        Assert.Equal("\"EndedAtUtc\" IS NULL", activeBedOccupancy.GetFilter());

        AssertCompositeRestrictiveRelationship<InpatientBedPlacement, InpatientAdmission>(
            db,
            [
                nameof(TenantOwnedEntity.TenantId),
                nameof(InpatientBedPlacement.HospitalId),
                nameof(InpatientBedPlacement.AdmissionId)
            ],
            [
                nameof(TenantOwnedEntity.TenantId),
                nameof(InpatientAdmission.HospitalId),
                nameof(InpatientAdmission.Id)
            ]);

        AssertCompositeRestrictiveRelationship<InpatientBedPlacement, Department>(
            db,
            [
                nameof(TenantOwnedEntity.TenantId),
                nameof(InpatientBedPlacement.HospitalId),
                nameof(InpatientBedPlacement.DepartmentId)
            ],
            [
                nameof(TenantOwnedEntity.TenantId),
                nameof(Department.HospitalId),
                nameof(Department.Id)
            ]);

        AssertCompositeRestrictiveRelationship<InpatientBedPlacement, Ward>(
            db,
            [
                nameof(TenantOwnedEntity.TenantId),
                nameof(InpatientBedPlacement.DepartmentId),
                nameof(InpatientBedPlacement.WardId)
            ],
            [
                nameof(TenantOwnedEntity.TenantId),
                nameof(Ward.DepartmentId),
                nameof(Ward.Id)
            ]);

        AssertCompositeRestrictiveRelationship<InpatientBedPlacement, Room>(
            db,
            [
                nameof(TenantOwnedEntity.TenantId),
                nameof(InpatientBedPlacement.WardId),
                nameof(InpatientBedPlacement.RoomId)
            ],
            [
                nameof(TenantOwnedEntity.TenantId),
                nameof(Room.WardId),
                nameof(Room.Id)
            ]);

        AssertCompositeRestrictiveRelationship<InpatientBedPlacement, Bed>(
            db,
            [
                nameof(TenantOwnedEntity.TenantId),
                nameof(InpatientBedPlacement.RoomId),
                nameof(InpatientBedPlacement.BedId)
            ],
            [
                nameof(TenantOwnedEntity.TenantId),
                nameof(Bed.RoomId),
                nameof(Bed.Id)
            ]);

        Assert.Contains(
            admission.GetCheckConstraints(),
            constraint => constraint.Name == "CK_inpatient_admissions_status");
        Assert.Contains(
            admission.GetCheckConstraints(),
            constraint => constraint.Name == "CK_inpatient_admissions_lifecycle");
        Assert.Contains(
            placement.GetCheckConstraints(),
            constraint => constraint.Name == "CK_inpatient_bed_placements_time_range");
    }

    private static void AssertCompositeRestrictiveRelationship<TChild, TParent>(
        HeapCareDbContext db,
        string[] dependentProperties,
        string[] principalProperties)
        where TChild : TenantOwnedEntity
        where TParent : TenantOwnedEntity
    {
        var child = db.Model.FindEntityType(typeof(TChild))!;
        var parent = db.Model.FindEntityType(typeof(TParent))!;
        var foreignKey = child.GetForeignKeys().Single(value => value.PrincipalEntityType == parent);

        Assert.Equal(DeleteBehavior.Restrict, foreignKey.DeleteBehavior);
        Assert.Equal(dependentProperties, foreignKey.Properties.Select(property => property.Name));
        Assert.Equal(principalProperties, foreignKey.PrincipalKey.Properties.Select(property => property.Name));
    }

    private static HeapCareDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<HeapCareDbContext>()
            .UseNpgsql("Host=localhost;Database=compilecare;Username=test;Password=test")
            .Options;
        return new HeapCareDbContext(options, new TenantContext());
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

    private static void AssertTenantScopedRestrictiveRelationship<TChild, TParent>(HeapCareDbContext db, string parentIdProperty)
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
