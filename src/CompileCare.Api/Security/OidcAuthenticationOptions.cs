namespace CompileCare.Api.Security;

public sealed class OidcAuthenticationOptions
{
    public const string SectionName = "Authentication";
    public string? Authority { get; set; }
    public string? Audience { get; set; }

    /// <summary>HTTP discovery is permitted only in Development with an explicit opt-in.</summary>
    public bool RequireHttpsMetadata { get; set; } = true;
}
