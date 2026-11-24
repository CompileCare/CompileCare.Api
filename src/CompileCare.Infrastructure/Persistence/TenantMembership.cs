namespace CompileCare.Infrastructure.Persistence;

/// <summary>
/// Maps a validated external OIDC identity to one tenant and one CompileCare role.
/// Issuer and subject together identify the external principal; email is not an identity key.
/// </summary>
public sealed class TenantMembership : TenantOwnedEntity
{
    public string Issuer { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public ICollection<TenantMembershipScope> Scopes { get; set; } = [];
}
