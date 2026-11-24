using CompileCare.Application.Tenancy;
using CompileCare.Application.Patients;

namespace CompileCare.Api.Security;

internal static class TenantRolePermissions
{
    public static IReadOnlyList<string> GetPermissions(string role) =>
        role switch
        {
            TenantRoles.HospitalAdmin =>
            [
                TenantPermissions.FacilityRead,
                TenantPermissions.FacilityWrite,
                TenantPermissions.FacilityDelete,
                TenantPermissions.MembershipManage
            ],
            TenantRoles.FacilityManager =>
            [
                TenantPermissions.FacilityRead,
                TenantPermissions.FacilityWrite
            ],
            TenantRoles.FacilityViewer =>
            [
                TenantPermissions.FacilityRead
            ],
            TenantRoles.PatientRegistrar =>
            [
                PatientPermissions.Read,
                PatientPermissions.Register
            ],
            _ => Array.Empty<string>()
        };
}
