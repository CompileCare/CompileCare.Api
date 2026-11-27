using HeapCare.Application.Tenancy;
using Microsoft.AspNetCore.Authorization;

namespace HeapCare.Api.Security;

/// <summary>Metadata used by the request audit middleware for global platform administration routes.</summary>
public sealed class PlatformOperatorEndpointMetadata
{
    public static PlatformOperatorEndpointMetadata Instance { get; } = new();
    private PlatformOperatorEndpointMetadata() { }
}

public sealed class PlatformOperatorRequirement : IAuthorizationRequirement
{
}

internal sealed class PlatformOperatorAuthorizationHandler(IPlatformOperatorReader operatorReader)
    : AuthorizationHandler<PlatformOperatorRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PlatformOperatorRequirement requirement)
    {
        var issuers = context.User.FindAll(AuthorizationPolicies.ValidatedIssuerClaim)
            .Select(claim => claim.Value).Distinct(StringComparer.Ordinal).ToArray();
        var subjects = context.User.FindAll("sub")
            .Select(claim => claim.Value).Distinct(StringComparer.Ordinal).ToArray();

        if (issuers.Length != 1 || subjects.Length != 1 ||
            string.IsNullOrWhiteSpace(issuers[0]) || string.IsNullOrWhiteSpace(subjects[0]))
            return;

        var cancellationToken = (context.Resource as HttpContext)?.RequestAborted ?? CancellationToken.None;
        if (await operatorReader.IsActiveAsync(issuers[0], subjects[0], cancellationToken))
            context.Succeed(requirement);
    }
}
