using System.Text;
using Maran.Host.Authorization;
using Maran.Modules.Identity.Common.Options;
using Maran.Sdk.Contracts;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Maran.Host.Extensions;

/// <summary>Wires bearer-token authentication and the panel's authorization policies.</summary>
public static class AuthenticationExtensions
{
    /// <summary>Registers the JWT bearer handler and the role policies.</summary>
    /// <param name="services">The application service collection.</param>
    /// <param name="configuration">Configuration the <see cref="JwtOptions"/> are read from.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddPanelAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var options = configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();

        // Inbound claim mapping rewrites short registered names into long XML-schema URIs, so a
        // token written with "sub" would be read back as a claim nobody asks for by that name.
        JsonWebTokenHandler.DefaultInboundClaimTypeMap.Clear();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(jwt =>
            {
                jwt.MapInboundClaims = false;
                jwt.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = options.Issuer,
                    ValidAudience = options.Audience,
                    IssuerSigningKey = new SymmetricSecurityKey(DecodeKey(options.SigningKey)),
                    NameClaimType = PanelClaimTypes.Username,
                    RoleClaimType = PanelClaimTypes.Role,

                    // The default five-minute tolerance would turn the spec's fifteen-minute token
                    // into a twenty-minute one, and a revoked session would outlive its revocation
                    // by that margin. Both machines run NTP; there is nothing to tolerate.
                    ClockSkew = TimeSpan.Zero,
                };
            });

        services.AddAuthorization(RolePolicies.Configure);

        return services;
    }

    /// <summary>Adds authentication and authorization to the request pipeline, in that order.</summary>
    /// <param name="app">The application being built.</param>
    /// <returns>The same application, for chaining.</returns>
    public static WebApplication UsePanelAuthentication(this WebApplication app)
    {
        app.UseAuthentication();
        app.UseAuthorization();
        return app;
    }

    /// <summary>
    /// Decodes the configured signing key. Startup validation has already rejected an unusable one
    /// (<see cref="JwtOptions.HasValidSigningKey"/>); this fallback exists so a unit-test host with
    /// no key configured still builds rather than throwing out of a registration method.
    /// </summary>
    /// <param name="signingKey">The base64-encoded key from configuration.</param>
    /// <returns>The decoded key bytes.</returns>
    private static byte[] DecodeKey(string signingKey)
    {
        try
        {
            return Convert.FromBase64String(signingKey);
        }
        catch (FormatException)
        {
            return Encoding.UTF8.GetBytes(signingKey);
        }
    }
}
