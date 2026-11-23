namespace CompileCare.Application.Tenancy;

public static class TenantRoles
{
    public const string HospitalAdmin = "HospitalAdmin";
    public const string FacilityManager = "FacilityManager";
    public const string FacilityViewer = "FacilityViewer";
}

public static class TenantPermissions
{
    public const string FacilityRead = "facility.read";
    public const string FacilityWrite = "facility.write";
    public const string FacilityDelete = "facility.delete";
}

public sealed record TenantMembershipAuthorization(string Role);

public interface ITenantMembershipReader
{
    Task<TenantMembershipAuthorization?> FindActiveMembershipAsync(
        Guid tenantId,
        string issuer,
        string subject,
        CancellationToken cancellationToken);
}
