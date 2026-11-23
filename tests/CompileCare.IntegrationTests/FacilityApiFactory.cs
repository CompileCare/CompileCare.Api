using CompileCare.Application.Tenancy;
using CompileCare.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
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
            services.RemoveAll<DbContextOptions<CompileCareDbContext>>();
            services.AddDbContext<CompileCareDbContext>(
                options => options.UseNpgsql(_connectionString));

            services
                .AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = TestAuthenticationHandler.Scheme;
                    options.DefaultChallengeScheme = TestAuthenticationHandler.Scheme;
                    options.DefaultForbidScheme = TestAuthenticationHandler.Scheme;
                })
                .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                    TestAuthenticationHandler.Scheme,
                    _ => { });
        });
    }

    public async Task SeedTenantMembershipAsync(
        Guid tenantId,
        string role,
        bool isActive = true)
    {
        using var scope = Services.CreateScope();
        var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        tenantContext.SetTenant(tenantId);

        var db = scope.ServiceProvider.GetRequiredService<CompileCareDbContext>();
        db.TenantMemberships.Add(new TenantMembership
        {
            TenantId = tenantId,
            Issuer = TestAuthenticationHandler.Issuer,
            Subject = TestAuthenticationHandler.Subject,
            Role = role,
            IsActive = isActive
        });

        await db.SaveChangesAsync();
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
        db.Database.Migrate();
    }
}
