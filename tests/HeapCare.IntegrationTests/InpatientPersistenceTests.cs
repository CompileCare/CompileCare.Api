using HeapCare.Application.Tenancy;
using HeapCare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace HeapCare.IntegrationTests;

[Collection("PostgreSQL integration")]
public sealed class InpatientPersistenceTests(FacilityApiFactory factory)
{
    [Fact]
    public async Task ConcurrentPlacements_ForTheSameBed_AllowExactlyOneActiveOccupant()
    {
        factory.ResetDatabase();

        var tenantId = Guid.NewGuid();
        var graph = default(FacilityGraph)!;
        Guid[] admissionIds;

        using (var scope = factory.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(tenantId);
            var db = scope.ServiceProvider.GetRequiredService<HeapCareDbContext>();

            db.Tenants.Add(CreateTenant(tenantId));
            await db.SaveChangesAsync();

            graph = AddFacilityGraph(db, "Concurrency Hospital", "CONC", bedCount: 2);
            admissionIds = AddAdmissions(db, graph, patientCount: 2);
            await db.SaveChangesAsync();
        }

        var results = await Task.WhenAll(admissionIds.Select(admissionId =>
            TryCreatePlacementAsync(tenantId, graph, admissionId, graph.BedIds[0])));

        Assert.Single(results, result => result);
        Assert.Single(results, result => !result);

        using var verifyScope = factory.Services.CreateScope();
        verifyScope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(tenantId);
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<HeapCareDbContext>();

        var activePlacements = await verifyDb.InpatientBedPlacements
            .AsNoTracking()
            .Where(placement => placement.EndedAtUtc == null)
            .ToListAsync();

        var activeOccupants = Assert.Single(activePlacements);
        Assert.Equal(graph.BedIds[0], activeOccupants.BedId);
        Assert.Contains(admissionIds, id => id == activeOccupants.AdmissionId);

        using var transferScope = factory.Services.CreateScope();
        transferScope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(tenantId);
        var transferDb = transferScope.ServiceProvider.GetRequiredService<HeapCareDbContext>();
        var currentAdmissionId = activeOccupants.AdmissionId;

        transferDb.InpatientBedPlacements.Add(new InpatientBedPlacement
        {
            HospitalId = graph.HospitalId,
            AdmissionId = currentAdmissionId,
            DepartmentId = graph.DepartmentId,
            WardId = graph.WardId,
            RoomId = graph.RoomId,
            BedId = graph.BedIds[1],
            StartedAtUtc = DateTimeOffset.UtcNow
        });

        var secondCurrentBed = await Assert.ThrowsAsync<DbUpdateException>(
            () => transferDb.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.UniqueViolation, FindPostgresError(secondCurrentBed).SqlState);
        transferDb.ChangeTracker.Clear();

        var previousPlacement = await transferDb.InpatientBedPlacements
            .SingleAsync(placement => placement.AdmissionId == currentAdmissionId && placement.EndedAtUtc == null);
        previousPlacement.EndedAtUtc = previousPlacement.StartedAtUtc.AddMinutes(5);
        await transferDb.SaveChangesAsync();

        transferDb.InpatientBedPlacements.Add(new InpatientBedPlacement
        {
            HospitalId = graph.HospitalId,
            AdmissionId = currentAdmissionId,
            DepartmentId = graph.DepartmentId,
            WardId = graph.WardId,
            RoomId = graph.RoomId,
            BedId = graph.BedIds[1],
            StartedAtUtc = previousPlacement.EndedAtUtc.Value
        });
        await transferDb.SaveChangesAsync();

        var retainedPlacements = await transferDb.InpatientBedPlacements
            .AsNoTracking()
            .Where(placement => placement.AdmissionId == currentAdmissionId)
            .OrderBy(placement => placement.StartedAtUtc)
            .ToListAsync();
        Assert.Equal(2, retainedPlacements.Count);
        Assert.NotNull(retainedPlacements[0].EndedAtUtc);
        Assert.Null(retainedPlacements[1].EndedAtUtc);
        Assert.Equal(graph.BedIds[1], retainedPlacements[1].BedId);
    }

    [Fact]
    public async Task CompositeForeignKeys_RejectCrossHospitalBedAndUnregisteredPatientAdmission()
    {
        factory.ResetDatabase();

        var tenantId = Guid.NewGuid();
        FacilityGraph firstHospital;
        FacilityGraph secondHospital;
        Guid patientId;
        Guid admissionId;

        using (var scope = factory.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(tenantId);
            var db = scope.ServiceProvider.GetRequiredService<HeapCareDbContext>();

            db.Tenants.Add(CreateTenant(tenantId));
            await db.SaveChangesAsync();

            firstHospital = AddFacilityGraph(db, "First IPD Hospital", "IPD1", bedCount: 1);
            secondHospital = AddFacilityGraph(db, "Second IPD Hospital", "IPD2", bedCount: 1);
            (patientId, admissionId) = AddPatientAndAdmission(db, firstHospital);
            await db.SaveChangesAsync();

            db.InpatientBedPlacements.Add(new InpatientBedPlacement
            {
                HospitalId = firstHospital.HospitalId,
                AdmissionId = admissionId,
                DepartmentId = firstHospital.DepartmentId,
                WardId = secondHospital.WardId,
                RoomId = secondHospital.RoomId,
                BedId = secondHospital.BedIds[0],
                StartedAtUtc = DateTimeOffset.UtcNow
            });

            var wrongHospitalBed = await Assert.ThrowsAsync<DbUpdateException>(
                () => db.SaveChangesAsync());
            Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, FindPostgresError(wrongHospitalBed).SqlState);

            db.ChangeTracker.Clear();

            db.InpatientAdmissions.Add(new InpatientAdmission
            {
                HospitalId = secondHospital.HospitalId,
                PatientId = patientId,
                Status = InpatientAdmissionStatuses.Active,
                AdmittedAtUtc = DateTimeOffset.UtcNow
            });

            var missingHospitalRegistration = await Assert.ThrowsAsync<DbUpdateException>(
                () => db.SaveChangesAsync());
            Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, FindPostgresError(missingHospitalRegistration).SqlState);

            db.ChangeTracker.Clear();

            Assert.Equal(0, await db.InpatientBedPlacements.CountAsync());
            Assert.Equal(1, await db.InpatientAdmissions.CountAsync());
        }
    }

    private async Task<bool> TryCreatePlacementAsync(
        Guid tenantId,
        FacilityGraph graph,
        Guid admissionId,
        Guid bedId)
    {
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(tenantId);
        var db = scope.ServiceProvider.GetRequiredService<HeapCareDbContext>();

        db.InpatientBedPlacements.Add(new InpatientBedPlacement
        {
            HospitalId = graph.HospitalId,
            AdmissionId = admissionId,
            DepartmentId = graph.DepartmentId,
            WardId = graph.WardId,
            RoomId = graph.RoomId,
            BedId = bedId,
            StartedAtUtc = DateTimeOffset.UtcNow
        });

        try
        {
            await db.SaveChangesAsync();
            return true;
        }
        catch (DbUpdateException exception)
            when (FindPostgresError(exception).SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return false;
        }
    }

    private static Tenant CreateTenant(Guid tenantId) =>
        new()
        {
            Id = tenantId,
            Name = "IPD integration tenant",
            Slug = $"ipd-{tenantId:N}",
            Status = TenantStatuses.Active
        };

    private static FacilityGraph AddFacilityGraph(
        HeapCareDbContext db,
        string hospitalName,
        string codePrefix,
        int bedCount)
    {
        var hospital = new Hospital
        {
            Name = hospitalName,
            Code = $"{codePrefix}-{Guid.NewGuid():N}"[..Math.Min(codePrefix.Length + 9, 50)]
        };
        var department = new Department
        {
            HospitalId = hospital.Id,
            Name = $"{hospitalName} Department",
            Code = $"{codePrefix}D"
        };
        var ward = new Ward
        {
            DepartmentId = department.Id,
            Name = $"{hospitalName} Ward"
        };
        var room = new Room
        {
            WardId = ward.Id,
            Name = $"{hospitalName} Room"
        };
        var beds = Enumerable.Range(1, bedCount)
            .Select(index => new Bed
            {
                RoomId = room.Id,
                Number = $"{codePrefix}-B{index:00}",
                IsActive = true
            })
            .ToArray();

        db.Hospitals.Add(hospital);
        db.Departments.Add(department);
        db.Wards.Add(ward);
        db.Rooms.Add(room);
        db.Beds.AddRange(beds);

        return new FacilityGraph(
            hospital.Id,
            department.Id,
            ward.Id,
            room.Id,
            beds.Select(bed => bed.Id).ToArray());
    }

    private static Guid[] AddAdmissions(
        HeapCareDbContext db,
        FacilityGraph graph,
        int patientCount)
    {
        var admissionIds = new List<Guid>(patientCount);
        for (var index = 1; index <= patientCount; index++)
            admissionIds.Add(AddPatientAndAdmission(db, graph, index).AdmissionId);
        return admissionIds.ToArray();
    }

    private static (Guid PatientId, Guid AdmissionId) AddPatientAndAdmission(
        HeapCareDbContext db,
        FacilityGraph graph,
        int index = 1)
    {
        var patient = new Patient
        {
            GivenName = $"IPD{index}",
            FamilyName = "Test",
            NormalizedGivenName = $"IPD{index}",
            NormalizedFamilyName = "TEST"
        };
        db.Patients.Add(patient);

        db.HospitalPatientRegistrations.Add(new HospitalPatientRegistration
        {
            PatientId = patient.Id,
            HospitalId = graph.HospitalId,
            RegisteredAtUtc = DateTimeOffset.UtcNow
        });

        var admission = new InpatientAdmission
        {
            HospitalId = graph.HospitalId,
            PatientId = patient.Id,
            Status = InpatientAdmissionStatuses.Active,
            AdmittedAtUtc = DateTimeOffset.UtcNow
        };
        db.InpatientAdmissions.Add(admission);

        return (patient.Id, admission.Id);
    }

    private static PostgresException FindPostgresError(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException postgresException)
                return postgresException;
        }

        throw new Xunit.Sdk.XunitException(
            $"Expected a PostgreSQL exception, but got {exception.GetType().Name}: {exception.Message}");
    }

    private sealed record FacilityGraph(
        Guid HospitalId,
        Guid DepartmentId,
        Guid WardId,
        Guid RoomId,
        Guid[] BedIds);
}
