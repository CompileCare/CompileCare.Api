using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace CompileCare.IntegrationTests;

public sealed class PlatformEndpointTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task LiveEndpoint_IsHealthy()
    {
        using var client = factory.CreateClient();
        var response = await client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task InvalidTenantHeader_IsRejectedWithProblemDetails()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Tenant-ID", "bad");

        var response = await client.GetAsync("/api/v1/platform/context");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var document = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(400, document.RootElement.GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task CorrelationIdMiddleware_PreservesValidOpaqueCorrelationIds()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Correlation-ID", "request-correlation-123");

        var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            "request-correlation-123",
            response.Headers.GetValues("X-Correlation-ID").Single());
    }

    [Fact]
    public async Task CorrelationIdMiddleware_ReplacesUnsafeCorrelationIds()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Correlation-ID", "invalid/correlation");

        var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var correlationId = response.Headers.GetValues("X-Correlation-ID").Single();
        Assert.NotEqual("invalid/correlation", correlationId);
        Assert.True(correlationId.Length <= 64);
        Assert.All(correlationId, character =>
            Assert.True(char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.'));
    }
}
