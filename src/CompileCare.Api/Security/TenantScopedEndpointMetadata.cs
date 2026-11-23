namespace CompileCare.Api.Security;

/// <summary>Marks endpoints that require a validated active tenant context.</summary>
public sealed class TenantScopedEndpointMetadata
{
    public static TenantScopedEndpointMetadata Instance { get; } = new();

    private TenantScopedEndpointMetadata()
    {
    }
}
