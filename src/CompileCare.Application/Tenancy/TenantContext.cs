namespace CompileCare.Application.Tenancy;
public sealed class TenantContext : ITenantContext
{
    public Guid? TenantId { get; private set; }
    public void SetTenant(Guid tenantId)
    {
        if (tenantId == Guid.Empty) throw new ArgumentException("Tenant ID cannot be empty.", nameof(tenantId));
        if (TenantId.HasValue && TenantId.Value != tenantId) throw new InvalidOperationException("Tenant context cannot change during a request.");
        TenantId = tenantId;
    }
}
