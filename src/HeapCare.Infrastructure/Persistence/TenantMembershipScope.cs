namespace HeapCare.Infrastructure.Persistence;

/// <summary>
/// Maps a tenant membership to one explicit organization boundary.
/// Tenant scope has no hospital/department IDs; hospital scope has only a hospital;
/// department scope has both its hospital and department IDs.
/// </summary>
public sealed class TenantMembershipScope : TenantOwnedEntity
{
    public Guid MembershipId { get; set; }
    public TenantMembership Membership { get; set; } = null!;
    public string ScopeType { get; set; } = string.Empty;
    public Guid? HospitalId { get; set; }
    public Guid? DepartmentId { get; set; }
}
