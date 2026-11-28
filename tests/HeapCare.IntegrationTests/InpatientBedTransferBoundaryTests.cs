using HeapCare.Application.Inpatient;
using HeapCare.Application.Tenancy;
using HeapCare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HeapCare.IntegrationTests;

[Collection("PostgreSQL integration")]
public sealed class InpatientBedTransferBoundaryTests(FacilityApiFactory factory)
{
    [Fact]
    public async Task TransferBed_RejectsSiblingHospitalAndForeignTenantBeds_WithoutChangingCurrentPlacement()
    {
        factory.ResetDatabase();

        var tenantId = Guid.NewGuid();
        var foreignTenantId = Guid.NewGuid();
        var data = await SeedScenarioAsync(tenantId, foreignTenantId);

        using (var serviceScope = factory.Services.CreateScope())
        {
            serviceScope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(tenantId);
            serviceScope.ServiceProvider.GetRequiredService<IOrganizationScopeContext>().SetMembership(
                Guid.NewGuid(),
                TenantRoles.HospitalAdmin,
                [new OrganizationScope(OrganizationScopeTypes.Hospital, data.HospitalId)]);

            var service = serviceScope.ServiceProvider.GetRequiredService<IInpatientService>();
            var actor = new InpatientActor(
                "https://identity.heapcare.test/",
                "ipd-hospital-boundary-user");

            var assigned = await service.AssignBedAsync(
                data.HospitalId,
                data.AdmissionId,
                data.CurrentBed,
                actor,
                CancellationToken.None);

            Assert.True(assigned.IsSuccess, assigned.Error);
            Assert.Equal(data.CurrentBed.BedId, assigned.Value!.CurrentPlacement!.Location.BedId);

            var siblingHospitalTransfer = await service.TransferBedAsync(
                data.HospitalId,
                data.AdmissionId,
                new InpatientBedTransferInput(data.SiblingHospitalBed, "Attempt cross-hospital transfer"),
                actor,
                CancellationToken.None);

            Assert.Equal(404, siblingHospitalTransfer.StatusCode);
            Assert.Null(siblingHospitalTransfer.Value);

            var foreignTenantTransfer = await service.TransferBedAsync(
                data.HospitalId,
                data.AdmissionId,
                new InpatientBedTransferInput(data.ForeignTenantBed, "Attempt cross-tenant transfer"),
                actor,
                CancellationToken.None);

            Assert.Equal(404, foreignTenantTransfer.StatusCode);
            Assert.Null(foreignTenantTransfer.Value);
        }

        using var verificationScope = factory.Services.CreateScope();
        verificationScope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(tenantId);
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<HeapCareDbContext>();

        var placements = await verificationDb.InpatientBedPlacements
            .AsNoTracking()
            .Where(placement => placement.AdmissionId == data.AdmissionId)
            .OrderBy(placement => placement.StartedAtUtc)
            .ToListAsync();

        var onlyPlacement = Assert.Single(placements);
        Assert.Equal(data.CurrentBed.BedId, onlyPlacement.BedId);
        Assert.Null(onlyPlacement.EndedAtUtc);
        Assert.Null(onlyPlacement.EndReason);
    }

    private async Task<TestData> SeedScenarioAsync(Guid tenantId, Guid foreignTenantId)
    {
        Guid hospitalId;
        Guid admissionId;
        InpatientBedLocation currentBed;
        InpatientBedLocation siblingHospitalBed;

        using (var scope = factory.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(tenantId);
            var db = scope.ServiceProvider.GetRequiredService<HeapCareDbContext>();

            db.Tenants.Add(new Tenant
            {
                Id = tenantId,
                Name = "IPD boundary test tenant",
                Slug = $"ipd-boundary-{tenantId:N}",
                Status = TenantStatuses.Active
            });
            await db.SaveChangesAsync();

            var hospital = new Hospital
            {
                Name = "Primary IPD Hospital",
                Code = $"IPDA-{Guid.NewGuid():N}"[..12]
            };
            var siblingHospital = new Hospital
            {
                Name = "Sibling IPD Hospital",
                Code = $"IPDB-{Guid.NewGuid():N}"[..12]
            };

            var primary = CreateBedHierarchy(hospital, "Primary");
            var sibling = CreateBedHierarchy(siblingHospital, "Sibling");

            var patient = new Patient
            {
                GivenName = "Synthetic",
                FamilyName = "Boundary Patient"
            };
            var admission = new InpatientAdmission
            {
                HospitalId = hospital.Id,
                PatientId = patient.Id,
                Status = InpatientAdmissionStatuses.Active,
                AdmittedAtUtc = DateTimeOffset.UtcNow
            };

            db.Hospitals.AddRange(hospital, siblingHospital);
            db.Departments.AddRange(primary.Department, sibling.Department);
            db.Wards.AddRange(primary.Ward, sibling.Ward);
            db.Rooms.AddRange(primary.Room, sibling.Room);
            db.Beds.AddRange(primary.Bed, sibling.Bed);
            db.Patients.Add(patient);
            db.HospitalPatientRegistrations.Add(new HospitalPatientRegistration
            {
                PatientId = patient.Id,
                HospitalId = hospital.Id,
                RegisteredAtUtc = DateTimeOffset.UtcNow
            });
            db.InpatientAdmissions.Add(admission);
            await db.SaveChangesAsync();

            hospitalId = hospital.Id;
            admissionId = admission.Id;
            currentBed = ToLocation(primary);
            siblingHospitalBed = ToLocation(sibling);
        }

        InpatientBedLocation foreignTenantBed;
        using (var scope = factory.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(foreignTenantId);
            var db = scope.ServiceProvider.GetRequiredService<HeapCareDbContext>();

            db.Tenants.Add(new Tenant
            {
                Id = foreignTenantId,
                Name = "Foreign IPD tenant",
                Slug = $"ipd-foreign-{foreignTenantId:N}",
                Status = TenantStatuses.Active
            });
            await db.SaveChangesAsync();

            var foreignHospital = new Hospital
            {
                Name = "Foreign Tenant Hospital",
                Code = $"IPDC-{Guid.NewGuid():N}"[..12]
            };
            var foreign = CreateBedHierarchy(foreignHospital, "Foreign");

            db.Hospitals.Add(foreignHospital);
            db.Departments.Add(foreign.Department);
            db.Wards.Add(foreign.Ward);
            db.Rooms.Add(foreign.Room);
            db.Beds.Add(foreign.Bed);
            await db.SaveChangesAsync();

            foreignTenantBed = ToLocation(foreign);
        }

        return new TestData(
            hospitalId,
            admissionId,
            currentBed,
            siblingHospitalBed,
            foreignTenantBed);
    }

    private static BedHierarchy CreateBedHierarchy(Hospital hospital, string prefix)
    {
        var department = new Department
        {
            HospitalId = hospital.Id,
            Name = $"{prefix} Department",
            Code = $"{prefix.ToUpperInvariant()}-IPD"
        };
        var ward = new Ward
        {
            DepartmentId = department.Id,
            Name = $"{prefix} Ward"
        };
        var room = new Room
        {
            WardId = ward.Id,
            Name = $"{prefix} Room"
        };
        var bed = new Bed
        {
            RoomId = room.Id,
            Number = $"{prefix}-01",
            IsActive = true
        };

        return new BedHierarchy(department, ward, room, bed);
    }

    private static InpatientBedLocation ToLocation(BedHierarchy hierarchy) =>
        new(
            hierarchy.Department.Id,
            hierarchy.Ward.Id,
            hierarchy.Room.Id,
            hierarchy.Bed.Id);

    private sealed record BedHierarchy(
        Department Department,
        Ward Ward,
        Room Room,
        Bed Bed);

    private sealed record TestData(
        Guid HospitalId,
        Guid AdmissionId,
        InpatientBedLocation CurrentBed,
        InpatientBedLocation SiblingHospitalBed,
        InpatientBedLocation ForeignTenantBed);
}
