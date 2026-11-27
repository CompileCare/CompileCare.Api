using System.Net;
using System.Collections.Generic;
using System.IO;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using HeapCare.Api.Middleware;
using HeapCare.Api.Security;

namespace HeapCare.IntegrationTests.Security;

public sealed class MetricsEndpointAccessTests
{
    [Fact]
    public void Production_requires_an_explicit_monitoring_network()
    {
        var configuration = new ConfigurationBuilder().Build();

        Assert.Throws<InvalidOperationException>(() =>
            MetricsEndpointNetworkConfiguration.GetAllowedNetworks(
                configuration,
                CreateEnvironment(Environments.Production)));
    }

    [Fact]
    public void Production_accepts_and_deduplicates_valid_cidrs()
    {
        var configuration = CreateConfiguration(
            "10.20.0.0/16",
            "10.20.0.0/16",
            "192.168.10.0/24");

        var networks = MetricsEndpointNetworkConfiguration.GetAllowedNetworks(
            configuration,
            CreateEnvironment(Environments.Production));

        Assert.Equal(2, networks.Length);
        Assert.Contains(networks, network => network.Contains(IPAddress.Parse("10.20.1.5")));
        Assert.Contains(networks, network => network.Contains(IPAddress.Parse("192.168.10.8")));
    }

    [Theory]
    [InlineData("10.20.0.0")]
    [InlineData("not-a-cidr")]
    [InlineData("10.20.0.0/99")]
    [InlineData("0.0.0.0/0")]
    [InlineData("::/0")]
    [InlineData(" 10.20.0.0/16 ")]
    public void Production_rejects_invalid_or_ambiguous_cidrs(string configuredNetwork)
    {
        var configuration = CreateConfiguration(configuredNetwork);

        Assert.Throws<InvalidOperationException>(() =>
            MetricsEndpointNetworkConfiguration.GetAllowedNetworks(
                configuration,
                CreateEnvironment(Environments.Production)));
    }

    [Fact]
    public void Development_and_testing_do_not_require_monitoring_network_configuration()
    {
        var configuration = new ConfigurationBuilder().Build();

        Assert.Empty(MetricsEndpointNetworkConfiguration.GetAllowedNetworks(
            configuration,
            CreateEnvironment(Environments.Development)));
        Assert.Empty(MetricsEndpointNetworkConfiguration.GetAllowedNetworks(
            configuration,
            CreateEnvironment("Testing")));
    }

    [Theory]
    [InlineData("/metrics")]
    [InlineData("/metrics/")]
    public async Task Metrics_request_from_an_untrusted_address_is_hidden(string path)
    {
        var nextCalled = false;
        var middleware = new MetricsEndpointAccessMiddleware(
            _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            },
            new[] { IPNetwork.Parse("10.20.0.0/16") });
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.10");

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
        Assert.False(nextCalled);
    }

    [Fact]
    public async Task Metrics_request_from_an_ipv4_mapped_ipv6_address_matches_ipv4_cidr()
    {
        var nextCalled = false;
        var middleware = new MetricsEndpointAccessMiddleware(
            _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            },
            new[] { IPNetwork.Parse("10.20.0.0/16") });
        var context = new DefaultHttpContext();
        context.Request.Path = "/metrics";
        context.Connection.RemoteIpAddress = IPAddress.Parse("::ffff:10.20.5.8");

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task Metrics_request_without_a_remote_address_is_hidden()
    {
        var nextCalled = false;
        var middleware = new MetricsEndpointAccessMiddleware(
            _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            },
            new[] { IPNetwork.Parse("10.20.0.0/16") });
        var context = new DefaultHttpContext();
        context.Request.Path = "/metrics";
        context.Connection.RemoteIpAddress = null;

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
        Assert.False(nextCalled);
    }

    [Fact]
    public async Task Metrics_request_from_an_allowed_monitoring_network_reaches_scraper()
    {
        var nextCalled = false;
        var middleware = new MetricsEndpointAccessMiddleware(
            _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            },
            new[] { IPNetwork.Parse("10.20.0.0/16") });
        var context = new DefaultHttpContext();
        context.Request.Path = "/metrics";
        context.Connection.RemoteIpAddress = IPAddress.Parse("10.20.5.8");

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task Non_metrics_routes_are_not_affected_by_the_monitoring_network_guard()
    {
        var nextCalled = false;
        var middleware = new MetricsEndpointAccessMiddleware(
            _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            },
            new[] { IPNetwork.Parse("10.20.0.0/16") });
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v1/hospitals";
        context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.10");

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
    }

    private static IConfiguration CreateConfiguration(params string[] networks)
    {
        var values = networks
            .Select((network, index) => new KeyValuePair<string, string?>(
                $"Observability:Metrics:AllowedNetworks:{index}",
                network))
            .ToDictionary(pair => pair.Key, pair => pair.Value);

        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }

    private static IHostEnvironment CreateEnvironment(string environmentName) =>
        new TestHostEnvironment
        {
            EnvironmentName = environmentName,
            ApplicationName = "HeapCare.Api",
            ContentRootPath = Directory.GetCurrentDirectory(),
            ContentRootFileProvider = new NullFileProvider()
        };

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = string.Empty;
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
