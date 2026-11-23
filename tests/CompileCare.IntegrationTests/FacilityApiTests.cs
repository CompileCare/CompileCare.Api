using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CompileCare.Api.Middleware;
using Xunit;

namespace CompileCare.IntegrationTests;

public sealed class FacilityApiTests : IClassFixture<FacilityApiFactory>
{
    private readonly FacilityApiFactory _factory;

    public FacilityApiTests(FacilityApiFactory factory)
    {
        _factory = factory;
        _factory.ResetDatabase();
    }

    [Fact]
    public async Task FacilityHierarchyCrud_IsTenantScopedAndPreservesResourceLocations()
    {
        var tenantId = Guid.NewGuid();
        using var client = CreateTenantClient(tenantId);

        var hospitalResponse = await client.PostAsJsonAsync(
            "/api/v1/hospitals",
            new { name = "Central Hospital", code = "CENTRAL" });
        var hospitalId = await AssertCreatedAsync(hospitalResponse, "/api/v1/hospitals");

        var duplicateHospital = await client.PostAsJsonAsync(
            "/api/v1/hospitals",
            new { name = "Central Hospital", code = "OTHER" });
        Assert.Equal(HttpStatusCode.Conflict, duplicateHospital.StatusCode);

        var secondHospitalResponse = await client.PostAsJsonAsync(
            "/api/v1/hospitals",
            new { name = "West Hospital", code = "WEST" });
        _ = await AssertCreatedAsync(secondHospitalResponse, "/api/v1/hospitals");

        var hospitalPage = await client.GetFromJsonAsync<JsonElement>(
            "/api/v1/hospitals?page=1&pageSize=1");
        Assert.Equal(2, hospitalPage.GetProperty("totalCount").GetInt32());
        Assert.Equal(1, hospitalPage.GetProperty("pageSize").GetInt32());
        Assert.Single(hospitalPage.GetProperty("items").EnumerateArray());

        var hospitalDetails = await client.GetAsync($"/api/v1/hospitals/{hospitalId:D}");
        Assert.Equal(HttpStatusCode.OK, hospitalDetails.StatusCode);

        var departmentCollection = $"/api/v1/hospitals/{hospitalId:D}/departments";
        var departmentResponse = await client.PostAsJsonAsync(
            departmentCollection,
            new { name = "Cardiology", code = "CARD" });
        var departmentId = await AssertCreatedAsync(departmentResponse, departmentCollection);

        var wardCollection = $"/api/v1/departments/{departmentId:D}/wards";
        var wardResponse = await client.PostAsJsonAsync(wardCollection, new { name = "Intensive Care" });
        var wardId = await AssertCreatedAsync(wardResponse, wardCollection);

        var roomCollection = $"/api/v1/wards/{wardId:D}/rooms";
        var roomResponse = await client.PostAsJsonAsync(roomCollection, new { name = "Room 101" });
        var roomId = await AssertCreatedAsync(roomResponse, roomCollection);

        var bedCollection = $"/api/v1/rooms/{roomId:D}/beds";
        var bedResponse = await client.PostAsJsonAsync(
            bedCollection,
            new { number = "B-01", isActive = true });
        var bedId = await AssertCreatedAsync(bedResponse, bedCollection);

        var duplicateDepartment = await client.PostAsJsonAsync(
            departmentCollection,
            new { name = "Cardiology", code = "CARD-2" });
        Assert.Equal(HttpStatusCode.Conflict, duplicateDepartment.StatusCode);

        using var otherTenantClient = CreateTenantClient(Guid.NewGuid());

        // Identical natural keys are valid in another tenant, but the original
        // tenant's resources must remain invisible to that tenant.
        var otherTenantHospital = await otherTenantClient.PostAsJsonAsync(
            "/api/v1/hospitals",
            new { name = "Central Hospital", code = "CENTRAL" });
        _ = await AssertCreatedAsync(otherTenantHospital, "/api/v1/hospitals");

        var crossTenantRead = await otherTenantClient.GetAsync($"/api/v1/hospitals/{hospitalId:D}");
        Assert.Equal(HttpStatusCode.NotFound, crossTenantRead.StatusCode);

        var crossTenantList = await otherTenantClient.GetFromJsonAsync<JsonElement>("/api/v1/hospitals");
        Assert.Equal(1, crossTenantList.GetProperty("totalCount").GetInt32());

        var crossTenantChildCreate = await otherTenantClient.PostAsJsonAsync(
            departmentCollection,
            new { name = "Unauthorized Department", code = "UNAUTH" });
        Assert.Equal(HttpStatusCode.NotFound, crossTenantChildCreate.StatusCode);

        // Restrictive foreign keys prevent deleting a parent while it has children.
        Assert.Equal(HttpStatusCode.Conflict, (await client.DeleteAsync($"/api/v1/hospitals/{hospitalId:D}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.DeleteAsync($"{departmentCollection}/{departmentId:D}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.DeleteAsync($"{wardCollection}/{wardId:D}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.DeleteAsync($"{roomCollection}/{roomId:D}")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"{bedCollection}/{bedId:D}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"{roomCollection}/{roomId:D}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"{wardCollection}/{wardId:D}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"{departmentCollection}/{departmentId:D}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/v1/hospitals/{hospitalId:D}")).StatusCode);
    }

    private HttpClient CreateTenantClient(Guid tenantId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TenantContextMiddleware.HeaderName, tenantId.ToString("D"));
        return client;
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
}
