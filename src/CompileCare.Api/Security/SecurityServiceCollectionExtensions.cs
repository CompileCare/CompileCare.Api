using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;

namespace CompileCare.Api.Security;

public static class SecurityServiceCollectionExtensions
{
    public static IServiceCollection AddCompileCareSecurity(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var authenticationSection = configuration.GetSection("Authentication");

        var authenticationOptions = services
            .AddOptions<OidcAuthenticationOptions>()
            .Bind(authenticationSection);

        // Tests replace the bearer handler with an isolated test-only scheme.
        // Every runnable environment must supply a real issuer and audience.
        if (!environment.IsEnvironment("Testing"))
        {
            authenticationOptions
                .Validate(options =>
                    Uri.TryCreate(options.Authority, UriKind.Absolute, out var authority) &&
                    (authority.Scheme == Uri.UriSchemeHttps ||
                     (environment.IsDevelopment() && authority.Scheme == Uri.UriSchemeHttp)),
                    "Authentication:Authority must be an absolute HTTPS URL (HTTP is allowed only in Development).")
                .Validate(options => !string.IsNullOrWhiteSpace(options.Audience),
                    "Authentication:Audience must be configured.")
                .ValidateOnStart();
        }

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.Authority = authenticationSection["Authority"];
                options.Audience = authenticationSection["Audience"];
                options.RequireHttpsMetadata = !environment.IsDevelopment();
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    RequireSignedTokens = true,
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    NameClaimType = "sub",
                    RoleClaimType = "role",
                    ClockSkew = TimeSpan.FromMinutes(1)
                };
            });

        services.AddAuthorization(AuthorizationPolicies.Configure);
        return services;
    }
}
