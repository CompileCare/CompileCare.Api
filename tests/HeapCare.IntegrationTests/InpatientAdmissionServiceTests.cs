using HeapCare.Application.Inpatient;
using HeapCare.Application.Tenancy;
using HeapCare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HeapCare.IntegrationTests;

[Collection("PostgreSQL integration")]
public sealed class InpatientAdmissionServiceTests(FacilityApiFactory factory)
{
    [Fact]
    public async Task CreateAdmission_UsesExistingHospitalRegistrationAndStartsWithoutBedPlacement()
    {
        factory.ResetDatabase();
        var tenantId = Guid.NewGuid();
        var data = await SeedAsync(tenantId);

        using var scope = CreateHospitalScope(tenantId, data.HospitalId);
        var service = scope.ServiceProvider.GetRequiredService<IInpatientService>();
        var before = DateTimeOffset.UtcNow;

        var result = await service.CreateAdmissionAsync(
            data.HospitalId,
            new InpatientAdmissionInput(data.PatientId),
            new InpatientActor("https://identity.heapcare.test/", "ipd-admitting-user"),
            CancellationToken.None);

        var after = DateTimeOffset.UtcNow;
        Assert.True(result.IsSuccess, result.Error);
        Assert.NotNull(result.Value);
        Assert.Equal(data.HospitalId, result.Value!.HospitalId);
        Assert.Equal(data.PatientId, result.Value.PatientId);
        Assert.Equal(InpatientAdmissionStatuses.Active, result.Value.Status);
        Assert.InRange(result.Value.AdmittedAtUtc, before, after);
        Assert.Null(result.Value.CancelledAtUtc);
        Assert.Null(result.Value.DischargedAtUtc);
        Assert.Null(result.Value.CurrentPlacement);
        Assert.Empty(result.Value.PlacementHistory);

        using var verificationScope = factory.Services.CreateScope();
        verificationScope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(tenantId);
        var db = verificationScope.ServiceProvider.GetRequiredService<HeapCareDbContext>();
        var persisted = await db.InpatientAdmissions.SingleAsync(item => item.Id == result.Value.Id);
        Assert.Equal(InpatientAdmissionStatuses.Active, persisted.Status);
        Assert.Equal(data.PatientId, persisted.PatientId);
        Assert.Empty(await db.InpatientBedPlacements.ToListAsync());
    }

    [Fact]
    public async Task CreateAdmission_DoesNotAcceptPatientRegisteredOnlyAtAnotherHospital()
    {
        factory.ResetDatabase();
        var tenantId = Guid.NewGuid();
        var data = await SeedAsync(tenantId);

        using var wrongHospitalScope = CreateHospitalScope(tenantId, data.OtherHospitalId);
        var wrongHospitalService = wrongHospitalScope.ServiceProvider.GetRequiredService<IInpatientService>();
        var wrongHospitalResult = await wrongHospitalService.CreateAdmissionAsync(
            data.OtherHospitalId,
            new InpatientAdmissionInput(data.PatientId),
            new InpatientActor("https://identity.heapcare.test/", "ipd-admitting-user"),
            CancellationToken.None);

        Assert.Equal(404, wrongHospitalResult.StatusCode);
        Assert.Null(wrongHospitalResult.Value);

        using var correctHospitalScope = CreateHospitalScope(tenantId, data.HospitalId);
        var correctHospitalService = correctHospitalScope.ServiceProvider.GetRequiredService<IInpatientService>();
        var correctHospitalResult = await correctHospitalService.CreateAdmissionAsync(
            data.HospitalId,
            new InpatientAdmissionInput(data.PatientId),
            new InpatientActor("https://identity.heapcare.test/", "ipd-admitting-user"),
            CancellationToken.None);

        Assert.True(correctHospitalResult.IsSuccess, correctHospitalResult.Error);
        Assert.Equal(data.HospitalId, correctHospitalResult.Value!.HospitalId);
    }

    [Fact]
    public async Task CreateAdmission_RejectsSecondActiveAdmissionForSamePatientAndHospital()
    {
        factory.ResetDatabase();
        var tenantId = Guid.NewGuid();
        var data = await SeedAsync(tenantId);
        using var scope = CreateHospitalScope(tenantId, data.HospitalId);
        var service = scope.ServiceProvider.GetRequiredService<IInpatientService>();
        var actor = new InpatientActor("https://identity.heapcare.test/", "ipd-admitting-user");

        var first = await service.CreateAdmissionAsync(
            data.HospitalId, new InpatientAdmissionInput(data.PatientId), actor, CancellationToken.None);
        var second = await service.CreateAdmissionAsync(
            data.HospitalId, new InpatientAdmissionInput(data.PatientId), actor, CancellationToken.None);

        Assert.True(first.IsSuccess, first.Error);
        Assert.Equal(409, second.StatusCode);
        Assert.Null(second.Value);
    }

    [Fact]
    public async Task ConcurrentAdmissionRequests_ForSamePatient_ReturnOneSuccessAndOneConflict()
    {
        factory.ResetDatabase();
        var tenantId = Guid.NewGuid();
        var data = await SeedAsync(tenantId);
        var actor = new InpatientActor("https://identity.heapcare.test/", "ipd-concurrent-admitter");

        var results = await Task.WhenAll(
            TryCreateAsync(tenantId, data.HospitalId, data.PatientId, actor),
            TryCreateAsync(tenantId, data.HospitalId, data.PatientId, actor));

        Assert.Single(results, result => result.IsSuccess);
        Assert.Single(results, result => result.StatusCode == 409);

        using var verificationScope = factory.Services.CreateScope();
        verificationScope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(tenantId);
        var db = verificationScope.ServiceProvider.GetRequiredService<HeapCareDbContext>();
        var activeAdmissions = await db.InpatientAdmissions
            .Where(item => item.PatientId == data.PatientId &&
                           item.HospitalId == data.HospitalId &&
                           item.Status == InpatientAdmissionStatuses.Active)
            .ToListAsync();
        Assert.Single(activeAdmissions);
    }

    [Fact]
    public async Task CreateAdmission_RequiresHospitalWideScopeAndValidatedActor()
    {
        factory.ResetDatabase();
        var tenantId = Guid.NewGuid();
        var data = await SeedAsync(tenantId);

        using var departmentScope = CreateDepartmentScope(tenantId, data.HospitalId, data.DepartmentId);
        var departmentService = departmentScope.ServiceProvider.GetRequiredService<IInpatientService>();
        var departmentResult = await departmentService.CreateAdmissionAsync(
            data.HospitalId,
            new InpatientAdmissionInput(data.PatientId),
            new InpatientActor("https://identity.heapcare.test/", "department-admitter"),
            CancellationToken.None);

        Assert.Equal(404, departmentResult.StatusCode);

        using var hospitalScope = CreateHospitalScope(tenantId, data.HospitalId);
        var hospitalService = hospitalScope.ServiceProvider.GetRequiredService<IInpatientService>();
        var invalidActorResult = await hospitalService.CreateAdmissionAsync(
            data.HospitalId,
            new InpatientAdmissionInput(data.PatientId),
            new InpatientActor("", ""),
            CancellationToken.None);

        Assert.Equal(401, invalidActorResult.StatusCode);
    }

    [Fact]
    public async Task GetAdmission_DoesNotDiscloseSiblingHospitalAdmissionToHospitalScopedMember()
    {
        factory.ResetDatabase();
        var tenantId = Guid.NewGuid();
        var data = await SeedAsync(tenantId);

        using var creatorScope = CreateHospitalScope(tenantId, data.HospitalId);
        var creator = creatorScope.ServiceProvider.GetRequiredService<IInpatientService>();
        var created = await creator.CreateAdmissionAsync(
            data.HospitalId,
            new InpatientAdmissionInput(data.PatientId),
            new InpatientActor("https://identity.heapcare.test/", "ipd-detail-creator"),
            CancellationToken.None);
        Assert.True(created.IsSuccess, created.Error);

        using var siblingScope = CreateHospitalScope(tenantId, data.OtherHospitalId);
        var siblingService = siblingScope.ServiceProvider.GetRequiredService<IInpatientService>();

        // Requesting the admission under its real Hospital ID must still be denied
        // because this member is scoped only to the sibling Hospital.
        var actualHospitalRequest = await siblingService.GetAsync(
            data.HospitalId, created.Value!.Id, CancellationToken.None);
        Assert.Equal(404, actualHospitalRequest.StatusCode);
        Assert.Null(actualHospitalRequest.Value);

        // Supplying the sibling Hospital route context with the original admission
        // identifier must also remain non-disclosing.
        var siblingRouteRequest = await siblingService.GetAsync(
            data.OtherHospitalId, created.Value.Id, CancellationToken.None);
        Assert.Equal(404, siblingRouteRequest.StatusCode);
        Assert.Null(siblingRouteRequest.Value);
    }

    [Fact]
    public async Task GetAdmission_DoesNotDiscloseForeignTenantAdmissionToTenantWideMember()
    {
        factory.ResetDatabase();
        var tenantId = Guid.NewGuid();
        var data = await SeedAsync(tenantId);

        using var creatorScope = CreateHospitalScope(tenantId, data.HospitalId);
        var creator = creatorScope.ServiceProvider.GetRequiredService<IInpatientService>();
        var created = await creator.CreateAdmissionAsync(
            data.HospitalId,
            new InpatientAdmissionInput(data.PatientId),
            new InpatientActor("https://identity.heapcare.test/", "ipd-foreign-tenant-creator"),
            CancellationToken.None);
        Assert.True(created.IsSuccess, created.Error);

        var foreignTenantId = Guid.NewGuid();
        using var foreignScope = factory.Services.CreateScope();
        foreignScope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(foreignTenantId);
        var db = foreignScope.ServiceProvider.GetRequiredService<HeapCareDbContext>();
        db.Tenants.Add(new Tenant
        {
            Id = foreignTenantId,
            Name = "Foreign IPD detail tenant",
            Slug = $"ipd-detail-foreign-{foreignTenantId:N}",
            Status = TenantStatuses.Active
        });
        await db.SaveChangesAsync();
        foreignScope.ServiceProvider.GetRequiredService<IOrganizationScopeContext>().SetMembership(
            Guid.NewGuid(),
            TenantRoles.HospitalAdmin,
            [new OrganizationScope(OrganizationScopeTypes.Tenant)]);

        var foreignService = foreignScope.ServiceProvider.GetRequiredService<IInpatientService>();
        var result = await foreignService.GetAsync(
            data.HospitalId, created.Value!.Id, CancellationToken.None);

        Assert.Equal(404, result.StatusCode);
        Assert.Null(result.Value);
    }

    private async Task<InpatientOperationResult<InpatientAdmissionDto>> TryCreateAsync(
        Guid tenantId,
        Guid hospitalId,
        Guid patientId,
        InpatientActor actor)
    {
        using var scope = CreateHospitalScope(tenantId, hospitalId);
        var service = scope.ServiceProvider.GetRequiredService<IInpatientService>();
        return await service.CreateAdmissionAsync(
            hospitalId, new InpatientAdmissionInput(patientId), actor, CancellationToken.None);
    }

    private IServiceScope CreateHospitalScope(Guid tenantId, Guid hospitalId)
    {
        var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(tenantId);
        scope.ServiceProvider.GetRequiredService<IOrganizationScopeContext>().SetMembership(
            Guid.NewGuid(),
            TenantRoles.HospitalAdmin,
            [new OrganizationScope(OrganizationScopeTypes.Hospital, hospitalId)]);
        return scope;
    }

    private IServiceScope CreateDepartmentScope(Guid tenantId, Guid hospitalId, Guid departmentId)
    {
        var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(tenantId);
        scope.ServiceProvider.GetRequiredService<IOrganizationScopeContext>().SetMembership(
            Guid.NewGuid(),
            TenantRoles.ClinicalUser,
            [new OrganizationScope(OrganizationScopeTypes.Department, hospitalId, departmentId)]);
        return scope;
    }

    private async Task<TestData> SeedAsync(Guid tenantId)
    {
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(tenantId);
        var db = scope.ServiceProvider.GetRequiredService<HeapCareDbContext>();

        db.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = "IPD admission test tenant",
            Slug = $"ipd-admission-{tenantId:N}",
            Status = TenantStatuses.Active
        });
        await db.SaveChangesAsync();

        var hospital = new Hospital
        {
            Name = "Admission Test Hospital",
            Code = $"ADMH-{Guid.NewGuid():N}"[..12]
        };
        var otherHospital = new Hospital
        {
            Name = "Other Registration Hospital",
            Code = $"OTHR-{Guid.NewGuid():N}"[..12]
        };
        var department = new Department
        {
            HospitalId = hospital.Id,
            Name = "Admission Test Department",
            Code = "ADMIT"
        };
        var patient = new Patient
        {
            GivenName = "Synthetic",
            FamilyName = "IPD Patient"
        };

        db.Hospitals.AddRange(hospital, otherHospital);
        db.Departments.Add(department);
        db.Patients.Add(patient);
        db.HospitalPatientRegistrations.Add(new HospitalPatientRegistration
        {
            PatientId = patient.Id,
            HospitalId = hospital.Id,
            RegisteredAtUtc = DateTimeOffset.UtcNow
        });

        await db.SaveChangesAsync();
        return new TestData(hospital.Id, otherHospital.Id, department.Id, patient.Id);
    }

    private sealed record TestData(
        Guid HospitalId,
        Guid OtherHospitalId,
        Guid DepartmentId,
        Guid PatientId);
}
