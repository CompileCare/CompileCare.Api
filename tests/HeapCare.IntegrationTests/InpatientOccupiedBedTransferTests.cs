using HeapCare.Application.Inpatient;
using HeapCare.Application.Tenancy;
using HeapCare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HeapCare.IntegrationTests;

[Collection("PostgreSQL integration")]
public sealed class InpatientOccupiedBedTransferTests(FacilityApiFactory factory)
{
    [Fact]
    public async Task TransferToOccupiedBed_ReturnsConflictAndKeepsBothPlacements()
    {
        factory.ResetDatabase();
        var tenantId = Guid.NewGuid();
        var data = await SeedScenarioAsync(tenantId);

        using (var scope = factory.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(tenantId);
            scope.ServiceProvider.GetRequiredService<IOrganizationScopeContext>().SetMembership(
                Guid.NewGuid(),
                TenantRoles.HospitalAdmin,
                [new OrganizationScope(OrganizationScopeTypes.Hospital, data.HospitalId)]);

            var service = scope.ServiceProvider.GetRequiredService<IInpatientService>();
            var actor = new InpatientActor(
                "https://identity.heapcare.test/",
                "ipd-occupied-bed-transfer-user");

            var firstAssignment = await service.AssignBedAsync(
                data.HospitalId,
                data.AdmissionIds[0],
                data.Locations[0],
                actor,
                CancellationToken.None);
            Assert.True(firstAssignment.IsSuccess, firstAssignment.Error);

            var secondAssignment = await service.AssignBedAsync(
                data.HospitalId,
                data.AdmissionIds[1],
                data.Locations[1],
                actor,
                CancellationToken.None);
            Assert.True(secondAssignment.IsSuccess, secondAssignment.Error);

            var rejectedTransfer = await service.TransferBedAsync(
                data.HospitalId,
                data.AdmissionIds[0],
                new InpatientBedTransferInput(data.Locations[1], "Attempt to use occupied bed"),
                actor,
                CancellationToken.None);

            Assert.Equal(409, rejectedTransfer.StatusCode);
            Assert.Null(rejectedTransfer.Value);
        }

        using var verificationScope = factory.Services.CreateScope();
        verificationScope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(tenantId);
        var db = verificationScope.ServiceProvider.GetRequiredService<HeapCareDbContext>();

        var placements = await db.InpatientBedPlacements
            .AsNoTracking()
            .OrderBy(placement => placement.AdmissionId)
            .ThenBy(placement => placement.StartedAtUtc)
            .ToListAsync();

        Assert.Equal(2, placements.Count);
        Assert.All(placements, placement => Assert.Null(placement.EndedAtUtc));

        var firstAdmissionPlacement = Assert.Single(
            placements,
            placement => placement.AdmissionId == data.AdmissionIds[0]);
        var secondAdmissionPlacement = Assert.Single(
            placements,
            placement => placement.AdmissionId == data.AdmissionIds[1]);

        Assert.Equal(data.Locations[0].BedId, firstAdmissionPlacement.BedId);
        Assert.Equal(data.Locations[1].BedId, secondAdmissionPlacement.BedId);
    }

    private async Task<TestData> SeedScenarioAsync(Guid tenantId)
    {
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(tenantId);
        var db = scope.ServiceProvider.GetRequiredService<HeapCareDbContext>();

        db.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = "IPD occupied-bed test tenant",
            Slug = $"ipd-occupied-{tenantId:N}",
            Status = TenantStatuses.Active
        });
        await db.SaveChangesAsync();

        var hospital = new Hospital
        {
            Name = "Occupied Bed Test Hospital",
            Code = $"OBT-{Guid.NewGuid():N}"[..12]
        };
        var department = new Department
        {
            HospitalId = hospital.Id,
            Name = "Occupied Bed Department",
            Code = "OBD"
        };
        var ward = new Ward
        {
            DepartmentId = department.Id,
            Name = "Occupied Bed Ward"
        };
        var room = new Room
        {
            WardId = ward.Id,
            Name = "Occupied Bed Room"
        };
        var beds = new[]
        {
            new Bed { RoomId = room.Id, Number = "OB-01", IsActive = true },
            new Bed { RoomId = room.Id, Number = "OB-02", IsActive = true }
        };

        var patients = Enumerable.Range(1, 2)
            .Select(index => new Patient
            {
                GivenName = $"Synthetic{index}",
                FamilyName = "Occupied Bed"
            })
            .ToArray();

        var admissions = patients.Select(patient => new InpatientAdmission
        {
            HospitalId = hospital.Id,
            PatientId = patient.Id,
            Status = InpatientAdmissionStatuses.Active,
            AdmittedAtUtc = DateTimeOffset.UtcNow
        }).ToArray();

        db.Hospitals.Add(hospital);
        db.Departments.Add(department);
        db.Wards.Add(ward);
        db.Rooms.Add(room);
        db.Beds.AddRange(beds);
        db.Patients.AddRange(patients);
        db.HospitalPatientRegistrations.AddRange(patients.Select(patient =>
            new HospitalPatientRegistration
            {
                PatientId = patient.Id,
                HospitalId = hospital.Id,
                RegisteredAtUtc = DateTimeOffset.UtcNow
            }));
        db.InpatientAdmissions.AddRange(admissions);
        await db.SaveChangesAsync();

        var locations = beds.Select(bed => new InpatientBedLocation(
            department.Id,
            ward.Id,
            room.Id,
            bed.Id)).ToArray();

        return new TestData(
            hospital.Id,
            admissions.Select(admission => admission.Id).ToArray(),
            locations);
    }

    private sealed record TestData(
        Guid HospitalId,
        IReadOnlyList<Guid> AdmissionIds,
        IReadOnlyList<InpatientBedLocation> Locations);
}
