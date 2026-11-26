using System;
using System.Collections.Generic;
using System.IO;
using CompileCare.Api.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace CompileCare.IntegrationTests.Security;

public sealed class DatabaseConnectionSecurityTests
{
    [Fact]
    public void Production_requires_a_database_connection_string()
    {
        var configuration = new ConfigurationBuilder().Build();

        Assert.Throws<InvalidOperationException>(() =>
            DatabaseConnectionSecurity.Validate(configuration, CreateEnvironment(Environments.Production)));
    }

    [Fact]
    public void Production_accepts_verified_full_tls()
    {
        var configuration = CreateConfiguration(
            "Host=db.example.com;Database=compilecare;Username=compilecare;Password=test-only;SSL Mode=VerifyFull");

        DatabaseConnectionSecurity.Validate(configuration, CreateEnvironment(Environments.Production));
    }

    [Theory]
    [InlineData("Disable")]
    [InlineData("Allow")]
    [InlineData("Prefer")]
    [InlineData("Require")]
    [InlineData("VerifyCA")]
    public void Production_rejects_tls_modes_without_full_hostname_verification(string sslMode)
    {
        var configuration = CreateConfiguration(
            $"Host=db.example.com;Database=compilecare;Username=compilecare;Password=test-only;SSL Mode={sslMode}");

        Assert.Throws<InvalidOperationException>(() =>
            DatabaseConnectionSecurity.Validate(configuration, CreateEnvironment(Environments.Production)));
    }

    [Fact]
    public void Testing_allows_the_isolated_integration_database()
    {
        var configuration = new ConfigurationBuilder().Build();

        DatabaseConnectionSecurity.Validate(configuration, CreateEnvironment("Testing"));
    }

    [Fact]
    public void Development_allows_local_plaintext_postgres()
    {
        var configuration = CreateConfiguration(
            "Host=localhost;Database=compilecare;Username=compilecare;Password=local-only;SSL Mode=Disable");

        DatabaseConnectionSecurity.Validate(configuration, CreateEnvironment(Environments.Development));
    }

    private static IConfiguration CreateConfiguration(string connectionString) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:CompileCare"] = connectionString
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
