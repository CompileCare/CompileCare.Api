using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace HeapCare.Infrastructure.Persistence;

public sealed class HeapCareDbContextFactory : IDesignTimeDbContextFactory<HeapCareDbContext>
{
    public HeapCareDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__CompileCare")
            ?? "Host=localhost;Database=compilecare;Username=compilecare;Password=compilecare";

        var options = new DbContextOptionsBuilder<HeapCareDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new HeapCareDbContext(options, new DesignTimeTenantContext());
    }

    private sealed class DesignTimeTenantContext : HeapCare.Application.Tenancy.ITenantContext
    {
        public Guid? TenantId => null;
        public void SetTenant(Guid tenantId) => throw new NotSupportedException("Design-time tenant context is not writable.");
    }
}