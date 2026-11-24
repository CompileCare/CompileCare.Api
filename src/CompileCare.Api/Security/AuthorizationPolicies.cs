using System.Security.Claims;
using CompileCare.Application.Appointments;
using CompileCare.Application.Patients;
using CompileCare.Application.Staff;
using CompileCare.Application.Tenancy;
using Microsoft.AspNetCore.Authorization;

namespace CompileCare.Api.Security;

public static class AuthorizationPolicies
{
    public const string FacilityRead = "FacilityRead";
    public const string FacilityWrite = "FacilityWrite";
    public const string FacilityDelete = "FacilityDelete";
    public const string TenantMembershipManage = "TenantMembershipManage";
    public const string PlatformOperator = "PlatformOperator";
    public const string PatientRead = "PatientRead";
    public const string PatientRegister = "PatientRegister";
    public const string PatientDuplicateReview = "PatientDuplicateReview";
    public const string PatientIdentifierRead = "PatientIdentifierRead";
    public const string PatientIdentifierAssign = "PatientIdentifierAssign";
    public const string PatientIdentifierPolicyRead = "PatientIdentifierPolicyRead";
    public const string PatientIdentifierPolicyManage = "PatientIdentifierPolicyManage";
    public const string StaffRead = "StaffRead";
    public const string StaffManage = "StaffManage";
    public const string AvailabilityManage = "AvailabilityManage";
    public const string AppointmentRead = "AppointmentRead";
    public const string AppointmentManage = "AppointmentManage";

    public const string TenantIdClaim = "compilecare:tenant_id";
    public const string TenantRoleClaim = "compilecare:tenant_role";
    public const string PermissionClaim = "compilecare:permission";
    public const string ValidatedIssuerClaim = "compilecare:validated_issuer";
    public const string MembershipIdentityAuthenticationType = "CompileCareTenantMembership";

    public static void Configure(AuthorizationOptions options)
    {
        options.AddPolicy(FacilityRead, policy =>
            policy.RequireAuthenticatedUser()
                .RequireAssertion(context => HasMembershipPermission(context.User, TenantPermissions.FacilityRead)));
        options.AddPolicy(FacilityWrite, policy =>
            policy.RequireAuthenticatedUser()
                .RequireAssertion(context => HasMembershipPermission(context.User, TenantPermissions.FacilityWrite)));
        options.AddPolicy(FacilityDelete, policy =>
            policy.RequireAuthenticatedUser()
                .RequireAssertion(context => HasMembershipPermission(context.User, TenantPermissions.FacilityDelete)));
        options.AddPolicy(TenantMembershipManage, policy =>
            policy.RequireAuthenticatedUser()
                .RequireAssertion(context => HasMembershipPermission(context.User, TenantPermissions.MembershipManage)));
        options.AddPolicy(PatientRead, policy =>
            policy.RequireAuthenticatedUser()
                .RequireAssertion(context => HasMembershipPermission(context.User, PatientPermissions.Read)));
        options.AddPolicy(PatientRegister, policy =>
            policy.RequireAuthenticatedUser()
                .RequireAssertion(context => HasMembershipPermission(context.User, PatientPermissions.Register)));
        options.AddPolicy(PatientDuplicateReview, policy =>
            policy.RequireAuthenticatedUser()
                .RequireAssertion(context => HasMembershipPermission(context.User, PatientDuplicatePermissions.Review)));
        options.AddPolicy(PatientIdentifierRead, policy =>
            policy.RequireAuthenticatedUser()
                .RequireAssertion(context => HasMembershipPermission(context.User, PatientIdentifierPermissions.Read)));
        options.AddPolicy(PatientIdentifierAssign, policy =>
            policy.RequireAuthenticatedUser()
                .RequireAssertion(context => HasMembershipPermission(context.User, PatientIdentifierPermissions.Assign)));
        options.AddPolicy(PatientIdentifierPolicyRead, policy =>
            policy.RequireAuthenticatedUser()
                .RequireAssertion(context => HasMembershipPermission(context.User, PatientIdentifierPermissions.PolicyRead)));
        options.AddPolicy(PatientIdentifierPolicyManage, policy =>
            policy.RequireAuthenticatedUser()
                .RequireAssertion(context => HasMembershipPermission(context.User, PatientIdentifierPermissions.PolicyManage)));
        options.AddPolicy(StaffRead, policy =>
            policy.RequireAuthenticatedUser()
                .RequireAssertion(context => HasMembershipPermission(context.User, StaffPermissions.Read)));
        options.AddPolicy(StaffManage, policy =>
            policy.RequireAuthenticatedUser()
                .RequireAssertion(context => HasMembershipPermission(context.User, StaffPermissions.Manage)));
        options.AddPolicy(AvailabilityManage, policy =>
            policy.RequireAuthenticatedUser()
                .RequireAssertion(context => HasMembershipPermission(context.User, AppointmentPermissions.AvailabilityManage)));
        options.AddPolicy(AppointmentRead, policy =>
            policy.RequireAuthenticatedUser()
                .RequireAssertion(context => HasMembershipPermission(context.User, AppointmentPermissions.AppointmentRead)));
        options.AddPolicy(AppointmentManage, policy =>
            policy.RequireAuthenticatedUser()
                .RequireAssertion(context => HasMembershipPermission(context.User, AppointmentPermissions.AppointmentManage)));
        options.AddPolicy(PlatformOperator, policy =>
            policy.RequireAuthenticatedUser()
                .AddRequirements(new PlatformOperatorRequirement()));
    }

    private static bool HasMembershipPermission(ClaimsPrincipal principal, string permission) =>
        principal.Identities.Any(identity =>
            string.Equals(identity.AuthenticationType, MembershipIdentityAuthenticationType, StringComparison.Ordinal) &&
            identity.HasClaim(PermissionClaim, permission));
}
