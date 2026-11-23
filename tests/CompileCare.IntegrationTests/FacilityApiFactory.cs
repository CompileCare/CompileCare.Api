using CompileCare.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CompileCare.IntegrationTests;

public sealed class FacilityApiFactory : WebApplicationFactory<Program>
{
    private readonly string _connectionString =
        Environment.GetEnvironmentVariable("ConnectionStrings__CompileCareTests")
        ?? "Host=localhost;Port=5432;Database=compilecare_test;Username=postgres;Password=postgres";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureTestServices(services =>
        {
            // Replace any provider-less DbContext registration created when the
            // application host has no regular CompileCare connection string.
            services.RemoveAll<DbContextOptions<CompileCareDbContext>>();
            services.AddDbContext<CompileCareDbContext>(
                options => options.UseNpgsql(_connectionString));
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
