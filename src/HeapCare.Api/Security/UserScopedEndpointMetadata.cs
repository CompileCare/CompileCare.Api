namespace HeapCare.Api.Security;

/// <summary>
/// Marks authenticated user-context endpoints that resolve data from the validated
/// external identity without requiring a tenant header or selecting a tenant.
/// </summary>
public sealed class UserScopedEndpointMetadata
{
    public static UserScopedEndpointMetadata Instance { get; } = new();

    private UserScopedEndpointMetadata()
    {
    }
}
