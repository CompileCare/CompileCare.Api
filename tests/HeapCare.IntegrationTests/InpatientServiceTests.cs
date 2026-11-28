using HeapCare.Application.Inpatient;
using HeapCare.Application.Tenancy;
using HeapCare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HeapCare.IntegrationTests;

[Collection("PostgreSQL integration")]
public sealed class InpatientServiceTests(FacilityApiFactory factory)
{
    [Fact]
    public async Task AssignAndTransfer_PreservesPlacementHistoryAndRejectsInvalidTransitions()
    {
        factory.ResetDatabase();
        var tenantId = Guid.NewGuid();
        var data = await SeedAsync(tenantId, departmentCount: 1, bedsPerDepartment: 3, admissionCount: 1);

        using var scope = CreateScope(tenantId, data, hospitalWide: true);
        var service = scope.ServiceProvider.GetRequiredService<IInpatientService>();
        var actor = new InpatientActor("https://identity.heapcare.test/", "ipd-service-user");

        var assign = await service.AssignBedAsync(
            data.HospitalId, data.AdmissionIds[0], data.Locations[0], actor, CancellationToken.None);

        Assert.True(assign.IsSuccess, assign.Error);
        Assert.NotNull(assign.Value);
        Assert.Equal(data.Locations[0], assign.Value!.CurrentPlacement!.Location);
        Assert.Single(assign.Value.PlacementHistory);

        var duplicateAssign = await service.AssignBedAsync(
            data.HospitalId, data.AdmissionIds[0], data.Locations[1], actor, CancellationToken.None);
        Assert.Equal(409, duplicateAssign.StatusCode);

        var transfer = await service.TransferBedAsync(
            data.HospitalId,
            data.AdmissionIds[0],
            new InpatientBedTransferInput(data.Locations[1], "Transfer to another bed"),
            actor,
            CancellationToken.None);

        Assert.True(transfer.IsSuccess, transfer.Error);
        Assert.NotNull(transfer.Value);
        Assert.Equal(data.Locations[1], transfer.Value!.CurrentPlacement!.Location);
        Assert.Equal(2, transfer.Value.PlacementHistory.Count);
        Assert.NotNull(transfer.Value.PlacementHistory[0].EndedAtUtc);
        Assert.Equal("Transfer to another bed", transfer.Value.PlacementHistory[0].EndReason);
        Assert.Null(transfer.Value.PlacementHistory[1].EndedAtUtc);
        Assert.Equal(data.Locations[1].BedId, transfer.Value.PlacementHistory[1].Location.BedId);

        var sameBedTransfer = await service.TransferBedAsync(
            data.HospitalId,
            data.AdmissionIds[0],
            new InpatientBedTransferInput(data.Locations[1], "No movement"),
            actor,
            CancellationToken.None);
        Assert.Equal(409, sameBedTransfer.StatusCode);

        var missingReason = await service.TransferBedAsync(
            data.HospitalId,
            data.AdmissionIds[0],
            new InpatientBedTransferInput(data.Locations[2], " "),
            actor,
            CancellationToken.None);
        Assert.Equal(400, missingReason.StatusCode);

        var invalidActor = await service.AssignBedAsync(
            data.HospitalId,
            data.AdmissionIds[0],
            data.Locations[2],
            new InpatientActor("", ""),
            CancellationToken.None);
        Assert.Equal(401, invalidActor.StatusCode);
    }

    [Fact]
    public async Task DepartmentScopedMember_CannotReadOrTransferIntoAnotherDepartment()
    {
        factory.ResetDatabase();
        var tenantId = Guid.NewGuid();
        var data = await SeedAsync(tenantId, departmentCount: 2, bedsPerDepartment: 1, admissionCount: 1);

        using (var assignScope = CreateDepartmentScope(tenantId, data, departmentIndex: 0))
        {
            var service = assignScope.ServiceProvider.GetRequiredService<IInpatientService>();
            var assigned = await service.AssignBedAsync(
                data.HospitalId,
                data.AdmissionIds[0],
                data.Locations[0],
                new InpatientActor("https://identity.heapcare.test/", "ipd-department-user"),
                CancellationToken.None);
            Assert.True(assigned.IsSuccess, assigned.Error);
        }

        using (var wrongDepartmentScope = CreateDepartmentScope(tenantId, data, departmentIndex: 1))
        {
            var service = wrongDepartmentScope.ServiceProvider.GetRequiredService<IInpatientService>();

            var read = await service.GetAsync(
                data.HospitalId, data.AdmissionIds[0], CancellationToken.None);
            Assert.Equal(404, read.StatusCode);

            var crossDepartmentTransfer = await service.TransferBedAsync(
                data.HospitalId,
                data.AdmissionIds[0],
                new InpatientBedTransferInput(data.Locations[1], "Cross-department transfer"),
                new InpatientActor("https://identity.heapcare.test/", "ipd-department-user"),
                CancellationToken.None);
            Assert.Equal(404, crossDepartmentTransfer.StatusCode);
        }

        using var verifyScope = factory.Services.CreateScope();
        verifyScope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(tenantId);
        var db = verifyScope.ServiceProvider.GetRequiredService<HeapCareDbContext>();
        var current = await db.InpatientBedPlacements.SingleAsync(
            placement => placement.AdmissionId == data.AdmissionIds[0] && placement.EndedAtUtc == null);
        Assert.Equal(data.Locations[0].BedId, current.BedId);
    }

    [Fact]
    public async Task ConcurrentAssignments_ForOneBed_ReturnOneSuccessAndOneConflict()
    {
        factory.ResetDatabase();
        var tenantId = Guid.NewGuid();
        var data = await SeedAsync(tenantId, departmentCount: 1, bedsPerDepartment: 1, admissionCount: 2);
        var actor = new InpatientActor("https://identity.heapcare.test/", "ipd-concurrent-user");

        var results = await Task.WhenAll(data.AdmissionIds.Select(admissionId =>
            TryAssignAsync(tenantId, data, admissionId, data.Locations[0], actor)));

        Assert.Single(results, result => result.IsSuccess);
        Assert.Single(results, result => result.StatusCode == 409);

        using var verifyScope = factory.Services.CreateScope();
        verifyScope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(tenantId);
        var db = verifyScope.ServiceProvider.GetRequiredService<HeapCareDbContext>();
        var active = await db.InpatientBedPlacements
            .AsNoTracking()
            .Where(placement => placement.EndedAtUtc == null)
            .ToListAsync();

        var occupant = Assert.Single(active);
        Assert.Equal(data.Locations[0].BedId, occupant.BedId);
        Assert.Contains(data.AdmissionIds, admissionId => admissionId == occupant.AdmissionId);
    }

    [Fact]
    public async Task InactiveBed_IsRejectedWithoutCreatingPlacement()
    {
        factory.ResetDatabase();
        var tenantId = Guid.NewGuid();
        var data = await SeedAsync(tenantId, departmentCount: 1, bedsPerDepartment: 2, admissionCount: 1);

        using (var scope = factory.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(tenantId);
            var db = scope.ServiceProvider.GetRequiredService<HeapCareDbContext>();
            var inactiveBed = await db.Beds.SingleAsync(bed => bed.Id == data.Locations[1].BedId);
            inactiveBed.IsActive = false;
            await db.SaveChangesAsync();
        }

        using var serviceScope = CreateScope(tenantId, data, hospitalWide: true);
        var service = serviceScope.ServiceProvider.GetRequiredService<IInpatientService>();
        var result = await service.AssignBedAsync(
            data.HospitalId,
            data.AdmissionIds[0],
            data.Locations[1],
            new InpatientActor("https://identity.heapcare.test/", "ipd-service-user"),
            CancellationToken.None);

        Assert.Equal(409, result.StatusCode);

        using var verifyScope = factory.Services.CreateScope();
        verifyScope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(tenantId);
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<HeapCareDbContext>();
        Assert.Equal(0, await verifyDb.InpatientBedPlacements.CountAsync());
    }

    [Fact]
    public async Task ListAdmissions_HospitalWideScopeSupportsPaginationAndStatusFilter()
    {
        factory.ResetDatabase();
        var tenantId = Guid.NewGuid();
        var data = await SeedAsync(tenantId, departmentCount: 1, bedsPerDepartment: 1, admissionCount: 3);

        using var scope = CreateScope(tenantId, data, hospitalWide: true);
        var service = scope.ServiceProvider.GetRequiredService<IInpatientService>();

        var secondPage = await service.ListAsync(
            data.HospitalId,
            new InpatientAdmissionPageRequest(2, 2, InpatientAdmissionStatuses.Active),
            CancellationToken.None);

        Assert.True(secondPage.IsSuccess, secondPage.Error);
        Assert.NotNull(secondPage.Value);
        Assert.Equal(2, secondPage.Value!.Page);
        Assert.Equal(2, secondPage.Value.PageSize);
        Assert.Equal(3, secondPage.Value.TotalCount);
        Assert.Equal(2, secondPage.Value.TotalPages);
        Assert.Single(secondPage.Value.Items);
        Assert.All(secondPage.Value.Items, item =>
        {
            Assert.Equal(InpatientAdmissionStatuses.Active, item.Status);
            Assert.Null(item.CurrentPlacement);
            Assert.Empty(item.PlacementHistory);
        });

        var invalidStatus = await service.ListAsync(
            data.HospitalId,
            new InpatientAdmissionPageRequest(1, 25, "Pending"),
            CancellationToken.None);
        Assert.Equal(400, invalidStatus.StatusCode);
    }

    [Fact]
    public async Task ListAdmissions_DepartmentScopeFiltersByCurrentPlacementAndHidesOtherDepartmentHistory()
    {
        factory.ResetDatabase();
        var tenantId = Guid.NewGuid();
        var data = await SeedAsync(tenantId, departmentCount: 2, bedsPerDepartment: 2, admissionCount: 2);
        var actor = new InpatientActor("https://identity.heapcare.test/", "ipd-list-scope-user");

        // Use hospital scope to create one admission that moves from Department 0
        // to Department 1, and another admission that remains in Department 0.
        using (var hospitalScope = CreateScope(tenantId, data, hospitalWide: true))
        {
            var service = hospitalScope.ServiceProvider.GetRequiredService<IInpatientService>();
            var firstAssigned = await service.AssignBedAsync(
                data.HospitalId, data.AdmissionIds[0], data.Locations[0], actor, CancellationToken.None);
            Assert.True(firstAssigned.IsSuccess, firstAssigned.Error);

            var firstTransferred = await service.TransferBedAsync(
                data.HospitalId,
                data.AdmissionIds[0],
                new InpatientBedTransferInput(data.Locations[2], "Move to Department 1"),
                actor,
                CancellationToken.None);
            Assert.True(firstTransferred.IsSuccess, firstTransferred.Error);

            var secondAssigned = await service.AssignBedAsync(
                data.HospitalId, data.AdmissionIds[1], data.Locations[1], actor, CancellationToken.None);
            Assert.True(secondAssigned.IsSuccess, secondAssigned.Error);
        }

        using (var departmentZeroScope = CreateDepartmentScope(tenantId, data, departmentIndex: 0))
        {
            var service = departmentZeroScope.ServiceProvider.GetRequiredService<IInpatientService>();
            var page = await service.ListAsync(
                data.HospitalId,
                new InpatientAdmissionPageRequest(1, 25),
                CancellationToken.None);

            Assert.True(page.IsSuccess, page.Error);
            Assert.Equal(1, page.Value!.TotalCount);
            var visible = Assert.Single(page.Value.Items);
            Assert.Equal(data.AdmissionIds[1], visible.Id);
            Assert.Equal(data.Locations[1], visible.CurrentPlacement!.Location);
            Assert.Single(visible.PlacementHistory);

            var movedAdmission = await service.GetAsync(
                data.HospitalId, data.AdmissionIds[0], CancellationToken.None);
            Assert.Equal(404, movedAdmission.StatusCode);
        }

        using (var departmentOneScope = CreateDepartmentScope(tenantId, data, departmentIndex: 1))
        {
            var service = departmentOneScope.ServiceProvider.GetRequiredService<IInpatientService>();
            var page = await service.ListAsync(
                data.HospitalId,
                new InpatientAdmissionPageRequest(1, 25),
                CancellationToken.None);

            Assert.True(page.IsSuccess, page.Error);
            Assert.Equal(1, page.Value!.TotalCount);
            var visible = Assert.Single(page.Value.Items);
            Assert.Equal(data.AdmissionIds[0], visible.Id);
            Assert.Equal(data.Locations[2], visible.CurrentPlacement!.Location);

            // Historical movement in Department 0 is not disclosed to a member
            // whose only scope is Department 1.
            var placement = Assert.Single(visible.PlacementHistory);
            Assert.Equal(data.Locations[2], placement.Location);
            Assert.Null(placement.EndedAtUtc);
        }
    }

    private async Task<InpatientOperationResult<InpatientAdmissionDto>> TryAssignAsync(
        Guid tenantId,
        TestData data,
        Guid admissionId,
        InpatientBedLocation location,
        InpatientActor actor)
    {
        using var scope = CreateScope(tenantId, data, hospitalWide: true);
        var service = scope.ServiceProvider.GetRequiredService<IInpatientService>();
        return await service.AssignBedAsync(
            data.HospitalId, admissionId, location, actor, CancellationToken.None);
    }

    private IServiceScope CreateScope(Guid tenantId, TestData data, bool hospitalWide)
    {
        var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(tenantId);
        var organizationScope = scope.ServiceProvider.GetRequiredService<IOrganizationScopeContext>();
        organizationScope.SetMembership(
            Guid.NewGuid(),
            TenantRoles.HospitalAdmin,
            hospitalWide
                ? [new OrganizationScope(OrganizationScopeTypes.Hospital, data.HospitalId)]
                : [new OrganizationScope(OrganizationScopeTypes.Department, data.HospitalId, data.DepartmentIds[0])]);
        return scope;
    }

    private IServiceScope CreateDepartmentScope(Guid tenantId, TestData data, int departmentIndex)
    {
        var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(tenantId);
        var organizationScope = scope.ServiceProvider.GetRequiredService<IOrganizationScopeContext>();
        organizationScope.SetMembership(
            Guid.NewGuid(),
            TenantRoles.ClinicalUser,
            [new OrganizationScope(
                OrganizationScopeTypes.Department,
                data.HospitalId,
                data.DepartmentIds[departmentIndex])]);
        return scope;
    }

    private async Task<TestData> SeedAsync(
        Guid tenantId,
        int departmentCount,
        int bedsPerDepartment,
        int admissionCount)
    {
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(tenantId);
        var db = scope.ServiceProvider.GetRequiredService<HeapCareDbContext>();

        db.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = "IPD service test tenant",
            Slug = $"ipd-service-{tenantId:N}",
            Status = TenantStatuses.Active
        });
        await db.SaveChangesAsync();

        var hospital = new Hospital
        {
            Name = "IPD Service Hospital",
            Code = $"IPDS-{Guid.NewGuid():N}"[..12]
        };
        db.Hospitals.Add(hospital);

        var locations = new List<InpatientBedLocation>();
        var departmentIds = new List<Guid>();
        for (var departmentIndex = 0; departmentIndex < departmentCount; departmentIndex++)
        {
            var department = new Department
            {
                HospitalId = hospital.Id,
                Name = $"IPD Department {departmentIndex + 1}",
                Code = $"IPD{departmentIndex + 1}"
            };
            var ward = new Ward
            {
                DepartmentId = department.Id,
                Name = $"IPD Ward {departmentIndex + 1}"
            };
            var room = new Room
            {
                WardId = ward.Id,
                Name = $"IPD Room {departmentIndex + 1}"
            };
            db.Departments.Add(department);
            db.Wards.Add(ward);
            db.Rooms.Add(room);
            departmentIds.Add(department.Id);

            for (var bedIndex = 0; bedIndex < bedsPerDepartment; bedIndex++)
            {
                var bed = new Bed
                {
                    RoomId = room.Id,
                    Number = $"D{departmentIndex + 1}-B{bedIndex + 1}",
                    IsActive = true
                };
                db.Beds.Add(bed);
                locations.Add(new InpatientBedLocation(
                    department.Id, ward.Id, room.Id, bed.Id));
            }
        }

        var admissionIds = new List<Guid>();
        for (var patientIndex = 0; patientIndex < admissionCount; patientIndex++)
        {
            var patient = new Patient
            {
                GivenName = $"IPD{patientIndex + 1}",
                FamilyName = "Service Test"
            };
            db.Patients.Add(patient);
            db.HospitalPatientRegistrations.Add(new HospitalPatientRegistration
            {
                PatientId = patient.Id,
                HospitalId = hospital.Id,
                RegisteredAtUtc = DateTimeOffset.UtcNow
            });
            var admission = new InpatientAdmission
            {
                HospitalId = hospital.Id,
                PatientId = patient.Id,
                Status = InpatientAdmissionStatuses.Active,
                AdmittedAtUtc = DateTimeOffset.UtcNow
            };
            db.InpatientAdmissions.Add(admission);
            admissionIds.Add(admission.Id);
        }

        await db.SaveChangesAsync();
        return new TestData(hospital.Id, departmentIds, locations, admissionIds);
    }

    private sealed record TestData(
        Guid HospitalId,
        IReadOnlyList<Guid> DepartmentIds,
        IReadOnlyList<InpatientBedLocation> Locations,
        IReadOnlyList<Guid> AdmissionIds);
}
