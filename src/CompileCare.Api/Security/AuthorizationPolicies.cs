using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace CompileCare.Api.Security;

public static class AuthorizationPolicies
{
    public const string FacilityRead = "FacilityRead";
    public const string FacilityWrite = "FacilityWrite";
    public const string FacilityDelete = "FacilityDelete";

    public const string TenantIdClaim = "tenant_id";
    public const string PermissionClaim = "permission";

    public static void Configure(AuthorizationOptions options)
    {
        AddPolicy(
            options,
            FacilityRead,
            ["HospitalAdmin", "FacilityManager", "FacilityViewer"],
            ["facility.read", "facility.write", "facility.delete"]);

        AddPolicy(
            options,
            FacilityWrite,
            ["HospitalAdmin", "FacilityManager"],
            ["facility.write", "facility.delete"]);

        AddPolicy(
            options,
            FacilityDelete,
            ["HospitalAdmin"],
            ["facility.delete"]);
    }

    private static void AddPolicy(
        AuthorizationOptions options,
        string policyName,
        string[] allowedRoles,
        string[] allowedPermissions)
    {
        options.AddPolicy(policyName, policy =>
        {
            policy.RequireAuthenticatedUser();
            policy.RequireAssertion(context =>
                allowedRoles.Any(context.User.IsInRole) ||
                allowedPermissions.Any(permission =>
                    context.User.HasClaim(PermissionClaim, permission)));
        });
    }
}
