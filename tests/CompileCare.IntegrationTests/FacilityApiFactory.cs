using CompileCare.Application.Tenancy;
using CompileCare.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;

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

            // Keep the real JwtBearer handler and issuer-stamping event. Only replace
            // discovery with a local signing key for deterministic integration tests.
            services.PostConfigure<JwtBearerOptions>(
                JwtBearerDefaults.AuthenticationScheme,
                options =>
                {
                    options.Authority = null;
                    options.MetadataAddress = null;
                    options.ConfigurationManager = null;
                    options.Audience = TestJwtTokenFactory.Audience;
                    options.RequireHttpsMetadata = false;
                    options.TokenValidationParameters = new TokenValidationParameters
                    {
                        RequireSignedTokens = true,
                        RequireExpirationTime = true,
                        ValidateIssuer = true,
                        ValidIssuer = TestJwtTokenFactory.Issuer,
                        ValidateAudience = true,
                        ValidAudience = TestJwtTokenFactory.Audience,
                        ValidateLifetime = true,
                        ValidateIssuerSigningKey = true,
                        IssuerSigningKey = TestJwtTokenFactory.ValidationKey,
                        NameClaimType = "sub",
                        RoleClaimType = "role",
                        ClockSkew = TimeSpan.FromSeconds(5)
                    };
                });
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
            Issuer = TestJwtTokenFactory.Issuer,
            Subject = TestJwtTokenFactory.Subject,
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
