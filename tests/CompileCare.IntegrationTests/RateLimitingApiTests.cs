using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.RateLimiting;
using Xunit;

namespace CompileCare.IntegrationTests;

public sealed class RateLimitingApiTests
{
    [Fact]
    public async Task GlobalLimiterUsesTheConfiguredAnonymousPartition()
    {
        using var factory = new RateLimitedApiFactory();
        var limiter = factory.Services
            .GetRequiredService<IOptions<RateLimiterOptions>>()
            .Value.GlobalLimiter;
        Assert.NotNull(limiter);

        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Loopback;

        using var first = await limiter!.AcquireAsync(context, 1);
        using var second = await limiter.AcquireAsync(context, 1);
        using var third = await limiter.AcquireAsync(context, 1);

        Assert.True(first.IsAcquired);
        Assert.True(second.IsAcquired);
        Assert.False(third.IsAcquired);
    }

    [Fact]
    public async Task AnonymousRequestsAreThrottledWithProblemDetailsAndRetryAfter()
    {
        using var factory = new RateLimitedApiFactory();
        var configuredLimit = factory.Services.GetRequiredService<IConfiguration>();
        Assert.Equal("2", configuredLimit["RateLimiting:AnonymousPermitLimit"]);
        Assert.NotNull(factory.Services.GetRequiredService<IOptions<RateLimiterOptions>>().Value.GlobalLimiter);

        using var client = factory.CreateClient();

        var first = await client.GetAsync("/");
        var second = await client.GetAsync("/");
        var third = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);
        Assert.NotNull(third.Headers.RetryAfter);
        Assert.True(third.Headers.RetryAfter!.Delta > TimeSpan.Zero);
        Assert.Equal(
            "application/problem+json",
            third.Content.Headers.ContentType?.MediaType);

        using var problem = JsonDocument.Parse(await third.Content.ReadAsStringAsync());
        Assert.Equal(429, problem.RootElement.GetProperty("status").GetInt32());
        Assert.Equal("Too many requests", problem.RootElement.GetProperty("title").GetString());
        Assert.True(problem.RootElement.TryGetProperty("traceId", out _));
    }

    private sealed class RateLimitedApiFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Authentication:Authority"] = "https://identity.compilecare.test/",
                    ["Authentication:Audience"] = "compilecare-api",
                    ["Authentication:RequireHttpsMetadata"] = "true",
                    ["RateLimiting:WindowSeconds"] = "60",
                    ["RateLimiting:AnonymousPermitLimit"] = "2",
                    ["RateLimiting:AuthenticatedPermitLimit"] = "2"
                }));
        }
    }
}
