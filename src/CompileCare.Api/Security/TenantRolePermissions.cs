using CompileCare.Application.Patients;
using CompileCare.Application.Staff;
using CompileCare.Application.Tenancy;

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
                TenantPermissions.MembershipManage,
                PatientIdentifierPermissions.PolicyRead,
                PatientIdentifierPermissions.PolicyManage,
                StaffPermissions.Read,
                StaffPermissions.Manage
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
                PatientPermissions.Register,
                PatientDuplicatePermissions.Review,
                PatientIdentifierPermissions.Read,
                PatientIdentifierPermissions.Assign
            ],
            _ => Array.Empty<string>()
        };
}
