using CompileCare.Application.Tenancy;
using Microsoft.AspNetCore.Authorization;

namespace CompileCare.Api.Security;

public static class AuthorizationPolicies
{
    public const string FacilityRead = "FacilityRead";
    public const string FacilityWrite = "FacilityWrite";
    public const string FacilityDelete = "FacilityDelete";

    public const string TenantIdClaim = "compilecare:tenant_id";
    public const string TenantRoleClaim = "compilecare:tenant_role";
    public const string PermissionClaim = "compilecare:permission";

    public static void Configure(AuthorizationOptions options)
    {
        options.AddPolicy(FacilityRead, policy =>
            policy.RequireAuthenticatedUser().RequireClaim(PermissionClaim, TenantPermissions.FacilityRead));

        options.AddPolicy(FacilityWrite, policy =>
            policy.RequireAuthenticatedUser().RequireClaim(PermissionClaim, TenantPermissions.FacilityWrite));

        options.AddPolicy(FacilityDelete, policy =>
            policy.RequireAuthenticatedUser().RequireClaim(PermissionClaim, TenantPermissions.FacilityDelete));
    }
}
