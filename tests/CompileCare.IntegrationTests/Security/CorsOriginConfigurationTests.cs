using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using CompileCare.Api.Security;

namespace CompileCare.IntegrationTests.Security;

public sealed class CorsOriginConfigurationTests
{
    [Theory]
    [InlineData("https://hospital.example.com", "https://hospital.example.com")]
    [InlineData("https://hospital.example.com:8443/", "https://hospital.example.com:8443")]
    public void Production_accepts_and_normalizes_explicit_https_origins(string configuredOrigin, string expectedOrigin)
    {
        var configuration = CreateConfiguration(configuredOrigin);

        var origins = CorsOriginConfiguration.GetAllowedOrigins(
            configuration,
            CreateEnvironment(Environments.Production));

        Assert.Equal([expectedOrigin], origins);
    }

    [Theory]
    [InlineData("http://hospital.example.com")]
    [InlineData("ftp://hospital.example.com")]
    [InlineData("*")]
    [InlineData("https://*.example.com")]
    [InlineData("https://hospital.example.com/path")]
    [InlineData("https://hospital.example.com?tenant=all")]
    [InlineData("https://hospital.example.com#fragment")]
    [InlineData("https://user@hospital.example.com")]
    [InlineData(" https://hospital.example.com ")]
    public void Production_rejects_non_origin_or_insecure_cors_entries(string configuredOrigin)
    {
        var configuration = CreateConfiguration(configuredOrigin);

        Assert.Throws<InvalidOperationException>(() =>
            CorsOriginConfiguration.GetAllowedOrigins(
                configuration,
                CreateEnvironment(Environments.Production)));
    }

    [Fact]
    public void Development_allows_local_http_origins()
    {
        var configuration = CreateConfiguration("http://localhost:8080");

        var origins = CorsOriginConfiguration.GetAllowedOrigins(
            configuration,
            CreateEnvironment(Environments.Development));

        Assert.Equal(["http://localhost:8080"], origins);
    }

    [Fact]
    public void Production_allows_an_empty_list_for_same_origin_deployments()
    {
        var configuration = new ConfigurationBuilder().Build();

        var origins = CorsOriginConfiguration.GetAllowedOrigins(
            configuration,
            CreateEnvironment(Environments.Production));

        Assert.Empty(origins);
    }

    private static IConfiguration CreateConfiguration(string origin) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cors:AllowedOrigins:0"] = origin
            })
            .Build();

    private static IHostEnvironment CreateEnvironment(string environmentName) =>
        new TestHostEnvironment
        {
            EnvironmentName = environmentName,
            ApplicationName = "CompileCare.Api",
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
