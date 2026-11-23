using CompileCare.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CompileCare.IntegrationTests;

public sealed class FacilityApiFactory : WebApplicationFactory<Program>
{
    private readonly string _connectionString =
        Environment.GetEnvironmentVariable("ConnectionStrings__CompileCareTests")
        ?? "Host=localhost;Port=5432;Database=compilecare_test;Username=postgres;Password=postgres";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:CompileCare"] = _connectionString
            });
        });
    }

    public void ResetDatabase()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompileCareDbContext>();
        var databaseName = db.Database.GetDbConnection().Database;

        if (!databaseName.EndsWith("_test", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Refusing to reset database '{databaseName}'. Integration tests require a dedicated database ending in '_test'.");
        }

        db.Database.EnsureDeleted();
        db.Database.EnsureCreated();
    }
}
