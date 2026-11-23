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
            });

        services.AddAuthorization(AuthorizationPolicies.Configure);
        return services;
    }
}
