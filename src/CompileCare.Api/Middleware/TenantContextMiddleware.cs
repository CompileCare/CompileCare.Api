using System.Security.Claims;
using CompileCare.Api.Security;
using CompileCare.Application.Tenancy;
using Serilog.Context;

namespace CompileCare.Api.Middleware;

/// <summary>
/// Establishes tenant context only after an authenticated OIDC issuer/subject
/// has been resolved to an active CompileCare tenant membership.
/// </summary>
public sealed class TenantContextMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Tenant-ID";

    public async Task InvokeAsync(
        HttpContext context,
        ITenantContext tenantContext,
        ITenantMembershipReader membershipReader)
    {
        var isTenantScoped = context.GetEndpoint()?.Metadata
            .GetMetadata<TenantScopedEndpointMetadata>() is not null;

        if (!isTenantScoped || context.User.Identity?.IsAuthenticated != true)
        {
            await next(context);
            return;
        }

        var tenantHeaders = context.Request.Headers[HeaderName];
        if (tenantHeaders.Count != 1 ||
            !Guid.TryParse(tenantHeaders[0], out var tenantId) ||
            tenantId == Guid.Empty)
        {
            await Results.Problem(
                title: "Invalid tenant context",
                detail: $"A single, non-empty GUID must be supplied in the {HeaderName} header.",
                statusCode: StatusCodes.Status400BadRequest).ExecuteAsync(context);
            return;
        }

        // JWT claim mapping is disabled. These values come from the validated
        // token, not from an email address or a caller-controlled identity header.
        var issuer = context.User.FindFirst("iss")?.Value;
        var subject = context.User.FindFirst("sub")?.Value;
        if (string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(subject))
        {
            await Results.Problem(
                title: "Identity is not provisioned",
                detail: "The authenticated identity does not contain the required issuer and subject claims.",
                statusCode: StatusCodes.Status403Forbidden).ExecuteAsync(context);
            return;
        }

        var membership = await membershipReader.FindActiveMembershipAsync(
            tenantId,
            issuer,
            subject,
            context.RequestAborted);

        if (membership is null)
        {
            await Results.Problem(
                title: "Tenant access denied",
                detail: "The authenticated user does not have an active membership in the requested tenant.",
                statusCode: StatusCodes.Status403Forbidden).ExecuteAsync(context);
            return;
        }

        var permissions = TenantRolePermissions.GetPermissions(membership.Role);
        if (permissions.Count == 0)
        {
            await Results.Problem(
                title: "Tenant role is not supported",
                detail: "The user's tenant membership has no permissions assigned.",
                statusCode: StatusCodes.Status403Forbidden).ExecuteAsync(context);
            return;
        }

        // Only server-loaded membership data creates CompileCare permission claims.
        // Roles and permissions supplied by the external token or request are ignored.
        var membershipClaims = new List<Claim>
        {
            new(AuthorizationPolicies.TenantIdClaim, tenantId.ToString("D")),
            new(AuthorizationPolicies.TenantRoleClaim, membership.Role)
        };
        membershipClaims.AddRange(permissions.Select(
            permission => new Claim(AuthorizationPolicies.PermissionClaim, permission)));

        context.User.AddIdentity(new ClaimsIdentity(
            membershipClaims,
            authenticationType: "CompileCareTenantMembership"));

        tenantContext.SetTenant(tenantId);
        using (LogContext.PushProperty("TenantId", tenantId))
        {
            await next(context);
        }
    }
}
