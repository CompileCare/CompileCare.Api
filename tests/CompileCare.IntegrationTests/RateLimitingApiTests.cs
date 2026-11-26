using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
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


public sealed class ForwardedHeadersAndHstsApiTests
{
    [Fact]
    public async Task TrustedIngressForwardedHttpsResponsesIncludeHsts()
    {
        using var factory = new ProductionApiFactory(IPAddress.Parse("192.0.2.10"));
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/");
        request.Headers.TryAddWithoutValidation("X-Forwarded-Proto", "https");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("Strict-Transport-Security", out var values));
        Assert.Contains("max-age=", Assert.Single(values!));
    }

    [Fact]
    public async Task UntrustedForwardedProtoDoesNotEnableHsts()
    {
        using var factory = new ProductionApiFactory(IPAddress.Loopback);
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/");
        request.Headers.TryAddWithoutValidation("X-Forwarded-Proto", "https");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains("Strict-Transport-Security"));
    }

    [Fact]
    public void ForwardedHeadersAreRestrictedToConfiguredProxiesAndOneHop()
    {
        using var factory = new ProductionApiFactory(IPAddress.Loopback);
        var options = factory.Services.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value;

        Assert.Equal(
            ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
            options.ForwardedHeaders);
        Assert.Equal(1, options.ForwardLimit);
        Assert.True(options.RequireHeaderSymmetry);
        Assert.Contains(IPAddress.Parse("192.0.2.10"), options.KnownProxies);
        Assert.DoesNotContain(IPAddress.Loopback, options.KnownProxies);
    }

    private sealed class ProductionApiFactory(IPAddress remoteAddress) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Authentication:Authority"] = "https://identity.compilecare.test/",
                    ["Authentication:Audience"] = "compilecare-api",
                    ["Authentication:RequireHttpsMetadata"] = "true",
                    ["ReverseProxy:KnownProxies:0"] = "192.0.2.10"
                }));
            builder.ConfigureTestServices(services =>
            {
                services.PostConfigure<ForwardedHeadersOptions>(options =>
                {
                    // Keep only the explicitly trusted ingress address so a
                    // client-controlled forwarded scheme cannot enable HSTS.
                    options.KnownProxies.Clear();
                    options.KnownIPNetworks.Clear();
                    options.KnownProxies.Add(IPAddress.Parse("192.0.2.10"));
                });
                services.AddSingleton<IStartupFilter>(new RemoteAddressStartupFilter(remoteAddress));
            });
        }
    }

    private sealed class RemoteAddressStartupFilter(IPAddress remoteAddress) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, nextRequest) =>
            {
                context.Connection.RemoteIpAddress = remoteAddress;
                await nextRequest();
            });
            next(app);
        };
    }
}
