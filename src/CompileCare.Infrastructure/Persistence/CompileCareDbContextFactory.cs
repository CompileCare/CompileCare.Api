using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CompileCare.Infrastructure.Persistence;

public sealed class CompileCareDbContextFactory : IDesignTimeDbContextFactory<CompileCareDbContext>
{
    public CompileCareDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__CompileCare")
            ?? "Host=localhost;Database=compilecare;Username=compilecare;Password=compilecare";

        var options = new DbContextOptionsBuilder<CompileCareDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new CompileCareDbContext(options, new DesignTimeTenantContext());
    }

    private sealed class DesignTimeTenantContext : Application.Tenancy.ITenantContext
    {
        public Guid? TenantId => null;
        public void SetTenant(Guid tenantId) => throw new NotSupportedException("Design-time tenant context is not writable.");
    }
}