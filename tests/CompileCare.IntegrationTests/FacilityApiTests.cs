using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CompileCare.Api.Middleware;
using CompileCare.Application.Tenancy;
using CompileCare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CompileCare.IntegrationTests;

[Collection("PostgreSQL integration")]
public sealed class FacilityApiTests(FacilityApiFactory factory)
{
    private readonly FacilityApiFactory _factory = factory;

    public void ResetDatabaseForTest() => _factory.ResetDatabase();

    [Fact]
    public async Task FacilityHierarchyCrud_IsTenantScopedAndPreservesResourceLocations()
    {
        using var client = CreateTenantClient(Guid.NewGuid());

        var hospitalCollection = "/api/v1/hospitals";
        var hospitalResponse = await client.PostAsJsonAsync(
            hospitalCollection,
            new { name = "Central Hospital", code = "CENTRAL" });
        var hospitalId = await AssertCreatedAsync(hospitalResponse, hospitalCollection);

        var secondHospitalResponse = await client.PostAsJsonAsync(
            hospitalCollection,
            new { name = "West Hospital", code = "WEST" });
        var secondHospitalId = await AssertCreatedAsync(secondHospitalResponse, hospitalCollection);

        var duplicateHospital = await client.PostAsJsonAsync(
            hospitalCollection,
            new { name = "Central Hospital", code = "OTHER" });
        await AssertProblemDetailsAsync(duplicateHospital, HttpStatusCode.Conflict, "Resource conflict");

        var duplicateHospitalUpdate = await client.PutAsJsonAsync(
            $"{hospitalCollection}/{hospitalId:D}",
            new { name = "West Hospital", code = "WEST" });
        Assert.Equal(HttpStatusCode.Conflict, duplicateHospitalUpdate.StatusCode);

        await AssertCollectionCountAsync(client, hospitalCollection, expectedCount: 2);

        var hospitalPage = await client.GetFromJsonAsync<JsonElement>(
            $"{hospitalCollection}?page=1&pageSize=1");
        Assert.Equal(2, hospitalPage.GetProperty("totalCount").GetInt32());
        Assert.Equal(1, hospitalPage.GetProperty("pageSize").GetInt32());
        Assert.Single(hospitalPage.GetProperty("items").EnumerateArray());

        var departmentCollection = $"{hospitalCollection}/{hospitalId:D}/departments";
        var departmentResponse = await client.PostAsJsonAsync(
            departmentCollection,
            new { name = "Cardiology", code = "CARD" });
        var departmentId = await AssertCreatedAsync(departmentResponse, departmentCollection);

        var duplicateDepartment = await client.PostAsJsonAsync(
            departmentCollection,
            new { name = "Cardiology", code = "CARD-2" });
        Assert.Equal(HttpStatusCode.Conflict, duplicateDepartment.StatusCode);

        var wardCollection = $"/api/v1/departments/{departmentId:D}/wards";
        var wardResponse = await client.PostAsJsonAsync(
            wardCollection,
            new { name = "Intensive Care" });
        var wardId = await AssertCreatedAsync(wardResponse, wardCollection);

        var duplicateWard = await client.PostAsJsonAsync(
            wardCollection,
            new { name = "Intensive Care" });
        Assert.Equal(HttpStatusCode.Conflict, duplicateWard.StatusCode);

        var roomCollection = $"/api/v1/wards/{wardId:D}/rooms";
        var roomResponse = await client.PostAsJsonAsync(
            roomCollection,
            new { name = "Room 101" });
        var roomId = await AssertCreatedAsync(roomResponse, roomCollection);

        var duplicateRoom = await client.PostAsJsonAsync(
            roomCollection,
            new { name = "Room 101" });
        Assert.Equal(HttpStatusCode.Conflict, duplicateRoom.StatusCode);

        var bedCollection = $"/api/v1/rooms/{roomId:D}/beds";
        var bedResponse = await client.PostAsJsonAsync(
            bedCollection,
            new { number = "B-01", isActive = true });
        var bedId = await AssertCreatedAsync(bedResponse, bedCollection);

        var duplicateBed = await client.PostAsJsonAsync(
            bedCollection,
            new { number = "B-01", isActive = true });
        Assert.Equal(HttpStatusCode.Conflict, duplicateBed.StatusCode);

        // Exercise reads and page metadata at every hierarchy level.
        await AssertResourceNameAsync(client, $"{hospitalCollection}/{hospitalId:D}", "Central Hospital");
        await AssertResourceNameAsync(client, $"{departmentCollection}/{departmentId:D}", "Cardiology");
        await AssertResourceNameAsync(client, $"{wardCollection}/{wardId:D}", "Intensive Care");
        await AssertResourceNameAsync(client, $"{roomCollection}/{roomId:D}", "Room 101");
        await AssertResourceNumberAsync(client, $"{bedCollection}/{bedId:D}", "B-01");

        await AssertCollectionCountAsync(client, departmentCollection, expectedCount: 1);
        await AssertCollectionCountAsync(client, wardCollection, expectedCount: 1);
        await AssertCollectionCountAsync(client, roomCollection, expectedCount: 1);
        await AssertCollectionCountAsync(client, bedCollection, expectedCount: 1);

        // Exercise updates through the HTTP layer as well as the service/persistence layer.
        var hospitalUpdate = await client.PutAsJsonAsync(
            $"{hospitalCollection}/{hospitalId:D}",
            new { name = "Central City Hospital", code = "CENTRAL-UPDATED" });
        Assert.Equal(HttpStatusCode.OK, hospitalUpdate.StatusCode);

        var departmentUpdate = await client.PutAsJsonAsync(
            $"{departmentCollection}/{departmentId:D}",
            new { name = "Cardiology Updated", code = "CARD-UPDATED" });
        Assert.Equal(HttpStatusCode.OK, departmentUpdate.StatusCode);

        var wardUpdate = await client.PutAsJsonAsync(
            $"{wardCollection}/{wardId:D}",
            new { name = "ICU Updated" });
        Assert.Equal(HttpStatusCode.OK, wardUpdate.StatusCode);

        var roomUpdate = await client.PutAsJsonAsync(
            $"{roomCollection}/{roomId:D}",
            new { name = "Room 102" });
        Assert.Equal(HttpStatusCode.OK, roomUpdate.StatusCode);

        var bedUpdate = await client.PutAsJsonAsync(
            $"{bedCollection}/{bedId:D}",
            new { number = "B-02", isActive = false });
        Assert.Equal(HttpStatusCode.OK, bedUpdate.StatusCode);

        await AssertResourceNameAsync(client, $"{hospitalCollection}/{hospitalId:D}", "Central City Hospital");
        await AssertResourceNameAsync(client, $"{departmentCollection}/{departmentId:D}", "Cardiology Updated");
        await AssertResourceNameAsync(client, $"{wardCollection}/{wardId:D}", "ICU Updated");
        await AssertResourceNameAsync(client, $"{roomCollection}/{roomId:D}", "Room 102");

        var updatedBed = await client.GetFromJsonAsync<JsonElement>($"{bedCollection}/{bedId:D}");
        Assert.Equal("B-02", updatedBed.GetProperty("number").GetString());
        Assert.False(updatedBed.GetProperty("isActive").GetBoolean());

        using var otherTenantClient = CreateTenantClient(Guid.NewGuid());

        // The same natural keys can be used by another tenant without disclosing
        // or linking to resources owned by the first tenant.
        var otherTenantHospital = await otherTenantClient.PostAsJsonAsync(
            hospitalCollection,
            new { name = "Central City Hospital", code = "CENTRAL-UPDATED" });
        _ = await AssertCreatedAsync(otherTenantHospital, hospitalCollection);

        var crossTenantRead = await otherTenantClient.GetAsync(
            $"{hospitalCollection}/{hospitalId:D}");
        await AssertProblemDetailsAsync(crossTenantRead, HttpStatusCode.NotFound, "Resource not found");

        var crossTenantList = await otherTenantClient.GetFromJsonAsync<JsonElement>(hospitalCollection);
        Assert.Equal(1, crossTenantList.GetProperty("totalCount").GetInt32());

        var crossTenantChildCreate = await otherTenantClient.PostAsJsonAsync(
            departmentCollection,
            new { name = "Unauthorized Department", code = "UNAUTH" });
        Assert.Equal(HttpStatusCode.NotFound, crossTenantChildCreate.StatusCode);

        // Restrictive foreign keys prevent deleting a parent while it has children.
        Assert.Equal(
            HttpStatusCode.Conflict,
            (await client.DeleteAsync($"{hospitalCollection}/{hospitalId:D}")).StatusCode);
        Assert.Equal(
            HttpStatusCode.Conflict,
            (await client.DeleteAsync($"{departmentCollection}/{departmentId:D}")).StatusCode);
        Assert.Equal(
            HttpStatusCode.Conflict,
            (await client.DeleteAsync($"{wardCollection}/{wardId:D}")).StatusCode);
        Assert.Equal(
            HttpStatusCode.Conflict,
            (await client.DeleteAsync($"{roomCollection}/{roomId:D}")).StatusCode);

        Assert.Equal(
            HttpStatusCode.NoContent,
            (await client.DeleteAsync($"{bedCollection}/{bedId:D}")).StatusCode);
        Assert.Equal(
            HttpStatusCode.NoContent,
            (await client.DeleteAsync($"{roomCollection}/{roomId:D}")).StatusCode);
        Assert.Equal(
            HttpStatusCode.NoContent,
            (await client.DeleteAsync($"{wardCollection}/{wardId:D}")).StatusCode);
        Assert.Equal(
            HttpStatusCode.NoContent,
            (await client.DeleteAsync($"{departmentCollection}/{departmentId:D}")).StatusCode);
        Assert.Equal(
            HttpStatusCode.NoContent,
            (await client.DeleteAsync($"{hospitalCollection}/{hospitalId:D}")).StatusCode);
        Assert.Equal(
            HttpStatusCode.NoContent,
            (await client.DeleteAsync($"{hospitalCollection}/{secondHospitalId:D}")).StatusCode);
    }

    [Fact]
    public async Task TenantOwnedPersistence_RejectsStaleUpdatesAndCrossTenantAssignments()
    {
        var tenantId = Guid.NewGuid();
        using var client = CreateTenantClient(tenantId);

        var createResponse = await client.PostAsJsonAsync(
            "/api/v1/hospitals",
            new { name = "Concurrency Hospital", code = "CONCURRENT" });
        var hospitalId = await AssertCreatedAsync(createResponse, "/api/v1/hospitals");

        using var firstScope = _factory.Services.CreateScope();
        var firstTenant = firstScope.ServiceProvider.GetRequiredService<ITenantContext>();
        firstTenant.SetTenant(tenantId);
        var firstDb = firstScope.ServiceProvider.GetRequiredService<CompileCareDbContext>();
        var firstCopy = await firstDb.Hospitals.SingleAsync(hospital => hospital.Id == hospitalId);

        using var secondScope = _factory.Services.CreateScope();
        var secondTenant = secondScope.ServiceProvider.GetRequiredService<ITenantContext>();
        secondTenant.SetTenant(tenantId);
        var secondDb = secondScope.ServiceProvider.GetRequiredService<CompileCareDbContext>();
        var staleCopy = await secondDb.Hospitals.SingleAsync(hospital => hospital.Id == hospitalId);

        var originalVersion = firstCopy.Version;
        Assert.NotEqual(Guid.Empty, originalVersion);
        Assert.NotEqual(default, firstCopy.CreatedAtUtc);
        Assert.NotEqual(default, firstCopy.UpdatedAtUtc);

        firstCopy.Name = "First successful update";
        await firstDb.SaveChangesAsync();

        Assert.NotEqual(originalVersion, firstCopy.Version);
        staleCopy.Name = "Stale update";
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(
            () => secondDb.SaveChangesAsync());

        using var invalidScope = _factory.Services.CreateScope();
        var invalidTenant = invalidScope.ServiceProvider.GetRequiredService<ITenantContext>();
        invalidTenant.SetTenant(tenantId);
        var invalidDb = invalidScope.ServiceProvider.GetRequiredService<CompileCareDbContext>();
        invalidDb.Hospitals.Add(new Hospital
        {
            TenantId = Guid.NewGuid(),
            Name = "Cross-tenant assignment"
        });

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => invalidDb.SaveChangesAsync());
    }

    private HttpClient CreateTenantClient(Guid tenantId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TenantContextMiddleware.HeaderName, tenantId.ToString("D"));
        return client;
    }


    private static async Task AssertProblemDetailsAsync(
        HttpResponseMessage response,
        HttpStatusCode expectedStatus,
        string expectedTitle)
    {
        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var problem = document.RootElement;
        Assert.Equal((int)expectedStatus, problem.GetProperty("status").GetInt32());
        Assert.Equal(expectedTitle, problem.GetProperty("title").GetString());
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("detail").GetString()));
    }

    private static async Task<Guid> AssertCreatedAsync(
        HttpResponseMessage response,
        string collectionLocation)
    {
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var id = body.GetProperty("id").GetGuid();

        Assert.Equal($"{collectionLocation}/{id:D}", response.Headers.Location?.OriginalString);
        return id;
    }

    private static async Task AssertResourceNameAsync(
        HttpClient client,
        string resourceLocation,
        string expectedName)
    {
        var response = await client.GetAsync(resourceLocation);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(expectedName, body.GetProperty("name").GetString());
    }

    private static async Task AssertResourceNumberAsync(
        HttpClient client,
        string resourceLocation,
        string expectedNumber)
    {
        var response = await client.GetAsync(resourceLocation);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(expectedNumber, body.GetProperty("number").GetString());
    }

    private static async Task AssertCollectionCountAsync(
        HttpClient client,
        string collectionLocation,
        int expectedCount)
    {
        var response = await client.GetFromJsonAsync<JsonElement>(collectionLocation);
        Assert.Equal(expectedCount, response.GetProperty("totalCount").GetInt32());
        Assert.Equal(expectedCount, response.GetProperty("items").GetArrayLength());
    }
}
