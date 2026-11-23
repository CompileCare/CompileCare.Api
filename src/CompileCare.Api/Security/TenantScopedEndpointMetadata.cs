namespace CompileCare.Api.Security;

/// <summary>Marks endpoints requiring an authenticated tenant membership and tenant context.</summary>
public sealed class TenantScopedEndpointMetadata
{
    public static TenantScopedEndpointMetadata Instance { get; } = new();

    private TenantScopedEndpointMetadata()
    {
    }
}
