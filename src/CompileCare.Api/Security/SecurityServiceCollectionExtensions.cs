using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace CompileCare.Api.Security;

public static class SecurityServiceCollectionExtensions
{
    public static IServiceCollection AddCompileCareSecurity(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var section = configuration.GetSection(OidcAuthenticationOptions.SectionName);

        if (!environment.IsEnvironment("Testing"))
        {
            services.AddOptions<OidcAuthenticationOptions>()
                .Bind(section)
                .Validate(options =>
                    Uri.TryCreate(options.Authority, UriKind.Absolute, out var authority) &&
                    (authority.Scheme == Uri.UriSchemeHttps ||
                     (environment.IsDevelopment() && !options.RequireHttpsMetadata &&
                      authority.Scheme == Uri.UriSchemeHttp)),
                    "Authentication:Authority must be an absolute HTTPS URL. HTTP is allowed only in Development with explicit opt-in.")
                .Validate(options => !string.IsNullOrWhiteSpace(options.Audience),
                    "Authentication:Audience must be configured.")
                .Validate(options => options.RequireHttpsMetadata || environment.IsDevelopment(),
                    "Authentication:RequireHttpsMetadata can be disabled only in Development.")
                .ValidateOnStart();
        }

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.Authority = section["Authority"];
                options.Audience = section["Audience"];
                options.RequireHttpsMetadata =
                    section.GetValue<bool?>(nameof(OidcAuthenticationOptions.RequireHttpsMetadata)) ?? true;
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    RequireSignedTokens = true,
                    RequireExpirationTime = true,
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    NameClaimType = "sub",
                    RoleClaimType = "role",
                    ClockSkew = TimeSpan.FromMinutes(1)
                };
                options.Events = new JwtBearerEvents
                {
                    OnTokenValidated = context =>
                    {
                        var issuer = context.SecurityToken.Issuer;
                        var identity = context.Principal?.Identities.FirstOrDefault(candidate =>
                            candidate.IsAuthenticated &&
                            candidate.HasClaim(claim => claim.Type == "sub"));

                        if (string.IsNullOrWhiteSpace(issuer) || identity is null)
                        {
                            context.Fail("The validated access token must contain an issuer and subject.");
                            return Task.CompletedTask;
                        }

                        // Replace any identically named claim from the token with the
                        // issuer read from the validated SecurityToken instance.
                        foreach (var existing in identity.FindAll(AuthorizationPolicies.ValidatedIssuerClaim).ToArray())
                            identity.RemoveClaim(existing);

                        identity.AddClaim(new Claim(
                            AuthorizationPolicies.ValidatedIssuerClaim,
                            issuer,
                            ClaimValueTypes.String,
                            issuer));
                        return Task.CompletedTask;
                    }
                };
            });

        services.AddAuthorization(AuthorizationPolicies.Configure);
        return services;
    }
}
