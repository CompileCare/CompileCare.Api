using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CompileCare.Api.Middleware;
using CompileCare.Application.Patients;
using CompileCare.Application.Tenancy;
using CompileCare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CompileCare.IntegrationTests;

[Collection("PostgreSQL integration")]
public sealed class PatientIdentifierApiTests(FacilityApiFactory factory)
{
    [Fact]
    public async Task HospitalAdmin_ConfiguresPolicy_RegistrarAssignsAndLooksUpUniqueIdempotentMrns()
    {
        factory.ResetDatabase();

        var tenantId = Guid.NewGuid();
        const string adminSubject = "mrn-policy-admin";
        const string registrarSubject = "mrn-patient-registrar";
        await SeedMembershipAsync(tenantId, adminSubject, TenantRoles.HospitalAdmin);
        await SeedMembershipAsync(tenantId, registrarSubject, TenantRoles.PatientRegistrar);
        var hospitalId = await SeedHospitalAsync(tenantId, "MRN Test Hospital");

        using var admin = CreateClient(tenantId, adminSubject);
        using var registrar = CreateClient(tenantId, registrarSubject);

        var policyResponse = await admin.PostAsJsonAsync(
            $"/api/v1/hospitals/{hospitalId:D}/patient-identifier-policy",
            new PatientIdentifierPolicyInput(" akh ", 6));
        Assert.Equal(HttpStatusCode.Created, policyResponse.StatusCode);
        var policy = await policyResponse.Content.ReadFromJsonAsync<PatientIdentifierPolicyDto>();
        Assert.NotNull(policy);
        Assert.Equal("AKH", policy!.Prefix);
        Assert.Equal(6, policy.NumberLength);

        var duplicatePolicyResponse = await admin.PostAsJsonAsync(
            $"/api/v1/hospitals/{hospitalId:D}/patient-identifier-policy",
            new PatientIdentifierPolicyInput("OTHER", 5));
        Assert.Equal(HttpStatusCode.Conflict, duplicatePolicyResponse.StatusCode);

        var firstPatient = await RegisterPatientAsync(registrar, hospitalId, "Asha");
        var firstAssignments = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ =>
            registrar.PostAsync(
                $"/api/v1/hospitals/{hospitalId:D}/patients/{firstPatient.Id:D}/identifiers/mrn",
                content: null)));
        var firstIdentifiers = new List<PatientIdentifierDto>();
        foreach (var response in firstAssignments)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var identifier = await response.Content.ReadFromJsonAsync<PatientIdentifierDto>();
            Assert.NotNull(identifier);
            firstIdentifiers.Add(identifier!);
        }

        Assert.Single(firstIdentifiers.Select(x => x.Id).Distinct());
        Assert.Single(firstIdentifiers.Select(x => x.Value).Distinct());
        Assert.Equal("AKH000001", firstIdentifiers[0].Value);

        var secondPatient = await RegisterPatientAsync(registrar, hospitalId, "Rohan");
        var secondAssignment = await registrar.PostAsync(
            $"/api/v1/hospitals/{hospitalId:D}/patients/{secondPatient.Id:D}/identifiers/mrn",
            content: null);
        Assert.Equal(HttpStatusCode.OK, secondAssignment.StatusCode);
        var secondIdentifier = await secondAssignment.Content.ReadFromJsonAsync<PatientIdentifierDto>();
        Assert.NotNull(secondIdentifier);
        Assert.Equal("AKH000002", secondIdentifier!.Value);
        Assert.NotEqual(firstIdentifiers[0].Id, secondIdentifier.Id);

        var lookup = await registrar.GetFromJsonAsync<PatientIdentifierLookupDto>(
            $"/api/v1/hospitals/{hospitalId:D}/patients/by-mrn/{secondIdentifier.Value.ToLowerInvariant()}");
        Assert.NotNull(lookup);
        Assert.Equal(secondPatient.Id, lookup!.Patient.Id);
        Assert.Equal(secondIdentifier.Id, lookup.Identifier.Id);

        var identifiers = await registrar.GetFromJsonAsync<List<PatientIdentifierDto>>(
            $"/api/v1/hospitals/{hospitalId:D}/patients/{firstPatient.Id:D}/identifiers");
        Assert.NotNull(identifiers);
        Assert.Single(identifiers!);
        Assert.Equal(firstIdentifiers[0].Id, identifiers[0].Id);

        var registrarCannotManagePolicy = await registrar.PostAsJsonAsync(
            $"/api/v1/hospitals/{hospitalId:D}/patient-identifier-policy",
            new PatientIdentifierPolicyInput("DENIED", 6));
        Assert.Equal(HttpStatusCode.Forbidden, registrarCannotManagePolicy.StatusCode);

        var adminCanAssignExistingMrn = await admin.PostAsync(
            $"/api/v1/hospitals/{hospitalId:D}/patients/{firstPatient.Id:D}/identifiers/mrn",
            content: null);
        Assert.Equal(HttpStatusCode.OK, adminCanAssignExistingMrn.StatusCode);
        var adminAssignedIdentifier =
            await adminCanAssignExistingMrn.Content.ReadFromJsonAsync<PatientIdentifierDto>();
        Assert.NotNull(adminAssignedIdentifier);
        Assert.Equal(firstIdentifiers[0].Id, adminAssignedIdentifier!.Id);
        Assert.Equal(firstIdentifiers[0].Value, adminAssignedIdentifier.Value);
    }

    [Fact]
    public async Task HospitalScopedPolicyAdmin_CannotConfigureAnotherHospital()
    {
        factory.ResetDatabase();

        var tenantId = Guid.NewGuid();
        const string subject = "hospital-scoped-mrn-admin";
        var (authorizedHospitalId, otherHospitalId) = await SeedTenantAndTwoHospitalsAsync(tenantId);
        await SeedMembershipAsync(
            tenantId,
            subject,
            TenantRoles.HospitalAdmin,
            [new OrganizationScope(OrganizationScopeTypes.Hospital, authorizedHospitalId)]);

        using var client = CreateClient(tenantId, subject);
        var denied = await client.PostAsJsonAsync(
            $"/api/v1/hospitals/{otherHospitalId:D}/patient-identifier-policy",
            new PatientIdentifierPolicyInput("OTHER", 6));
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);

        var allowed = await client.PostAsJsonAsync(
            $"/api/v1/hospitals/{authorizedHospitalId:D}/patient-identifier-policy",
            new PatientIdentifierPolicyInput("LOCAL", 5));
        Assert.Equal(HttpStatusCode.Created, allowed.StatusCode);
    }

    private async Task<PatientDto> RegisterPatientAsync(HttpClient client, Guid hospitalId, string givenName)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/v1/hospitals/{hospitalId:D}/patients",
            new PatientRegistrationInput(givenName, null, "Test", null));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var patient = await response.Content.ReadFromJsonAsync<PatientDto>();
        Assert.NotNull(patient);
        return patient!;
    }

    private async Task SeedMembershipAsync(
        Guid tenantId,
        string subject,
        string role,
        IReadOnlyList<OrganizationScope>? scopes = null)
    {
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(tenantId);
        var db = scope.ServiceProvider.GetRequiredService<CompileCareDbContext>();

        if (!await db.Tenants.AnyAsync(x => x.Id == tenantId))
        {
            db.Tenants.Add(new Tenant
            {
                Id = tenantId,
                Name = "MRN integration test tenant",
                Slug = $"mrn-{tenantId:N}",
                Status = TenantStatuses.Active
            });
            await db.SaveChangesAsync();
        }

        var membership = new TenantMembership
        {
            TenantId = tenantId,
            Issuer = TestJwtTokenFactory.Issuer,
            Subject = subject,
            Role = role,
            IsActive = true
        };
        foreach (var item in scopes ?? [new OrganizationScope(OrganizationScopeTypes.Tenant)])
        {
            membership.Scopes.Add(new TenantMembershipScope
            {
                TenantId = tenantId,
                MembershipId = membership.Id,
                ScopeType = item.ScopeType,
                HospitalId = item.HospitalId,
                DepartmentId = item.DepartmentId
            });
        }

        db.TenantMemberships.Add(membership);
        await db.SaveChangesAsync();
    }

    private async Task<Guid> SeedHospitalAsync(Guid tenantId, string name)
    {
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(tenantId);
        var db = scope.ServiceProvider.GetRequiredService<CompileCareDbContext>();
        var hospital = new Hospital { Name = name, Code = Guid.NewGuid().ToString("N")[..8] };
        db.Hospitals.Add(hospital);
        await db.SaveChangesAsync();
        return hospital.Id;
    }

    private async Task<(Guid First, Guid Second)> SeedTenantAndTwoHospitalsAsync(Guid tenantId)
    {
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(tenantId);
        var db = scope.ServiceProvider.GetRequiredService<CompileCareDbContext>();
        db.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = "MRN scope test tenant",
            Slug = $"mrn-scope-{tenantId:N}",
            Status = TenantStatuses.Active
        });
        await db.SaveChangesAsync();

        var first = new Hospital { Name = "Authorized MRN Hospital", Code = Guid.NewGuid().ToString("N")[..8] };
        var second = new Hospital { Name = "Other MRN Hospital", Code = Guid.NewGuid().ToString("N")[..8] };
        db.Hospitals.AddRange(first, second);
        await db.SaveChangesAsync();
        return (first.Id, second.Id);
    }

    private HttpClient CreateClient(Guid tenantId, string subject)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwtTokenFactory.CreateToken(subject: subject));
        client.DefaultRequestHeaders.Add(TenantContextMiddleware.HeaderName, tenantId.ToString("D"));
        return client;
    }
}
