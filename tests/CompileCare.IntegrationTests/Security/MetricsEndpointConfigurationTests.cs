using System.IO;
using CompileCare.Api.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace CompileCare.IntegrationTests.Security;

public sealed class MetricsEndpointConfigurationTests
{
    [Fact]
    public void Production_disables_metrics_by_default()
    {
        var configuration = new ConfigurationBuilder().Build();

        Assert.False(MetricsEndpointConfiguration.IsEnabled(
            configuration,
            CreateEnvironment(Environments.Production)));
    }

    [Fact]
    public void Production_can_explicitly_enable_metrics_for_a_private_scrape_path()
    {
        var configuration = CreateConfiguration("true");

        Assert.True(MetricsEndpointConfiguration.IsEnabled(
            configuration,
            CreateEnvironment(Environments.Production)));
    }

    [Fact]
    public void Explicit_false_disables_metrics_in_development()
    {
        var configuration = CreateConfiguration("false");

        Assert.False(MetricsEndpointConfiguration.IsEnabled(
            configuration,
            CreateEnvironment(Environments.Development)));
    }

    [Theory]
    [InlineData(Environments.Development)]
    [InlineData("Testing")]
    public void Development_and_testing_keep_metrics_enabled_by_default(string environmentName)
    {
        var configuration = new ConfigurationBuilder().Build();

        Assert.True(MetricsEndpointConfiguration.IsEnabled(
            configuration,
            CreateEnvironment(environmentName)));
    }

    private static IConfiguration CreateConfiguration(string enabled) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Observability:Metrics:Enabled"] = enabled
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
