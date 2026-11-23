namespace CompileCare.Api.Security;

public sealed class OidcAuthenticationOptions
{
    public string? Authority { get; set; }
    public string? Audience { get; set; }
}
