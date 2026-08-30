using Maran.Modules.Identity.Common;
using Maran.Modules.Identity.Common.Interfaces;
using Maran.Modules.Identity.Common.Options;
using Maran.Modules.Identity.Domain;
using Maran.Sdk.Contracts;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Maran.Modules.Identity.Services;

/// <summary>Issues HMAC-SHA256 access tokens carrying the claims the panel authorizes against.</summary>
public sealed class JwtAccessTokenIssuer : IAccessTokenIssuer
{
    /// <summary>Signing key, issuer, audience and lifetime.</summary>
    private readonly JwtOptions _options;

    /// <summary>The panel's clock; never <c>DateTime.UtcNow</c> (rules/csharp.md "Forbidden").</summary>
    private readonly IClock _clock;

    /// <summary>Creates the issuer.</summary>
    /// <param name="options">The bound and validated <see cref="JwtOptions"/>.</param>
    /// <param name="clock">The panel's clock, so tests can decide what "now" means.</param>
    public JwtAccessTokenIssuer(IOptions<JwtOptions> options, IClock clock)
    {
        _options = options.Value;
        _clock = clock;
    }

    /// <inheritdoc />
    public AccessToken Issue(User user, Guid sessionId)
    {
        var issuedAt = _clock.UtcNow;
        var expiresAt = issuedAt.AddMinutes(_options.AccessTokenMinutes);

        var claims = new Dictionary<string, object>
        {
            [PanelClaimTypes.UserId] = user.Id.ToString(),
            [PanelClaimTypes.Username] = user.Username,
            [PanelClaimTypes.Role] = user.Role.ToString(),
            [PanelClaimTypes.SessionId] = sessionId.ToString(),
        };

        // Only a Customer has an owning account. Writing the claim as an empty string for an
        // administrator would make "no account" and "an account whose id failed to render" the
        // same value on the wire, and the authorization code would have to tell them apart.
        if (user.AccountId is { } accountId)
        {
            claims[PanelClaimTypes.AccountId] = accountId.ToString();
        }

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            IssuedAt = issuedAt.UtcDateTime,
            NotBefore = issuedAt.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            Claims = claims,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Convert.FromBase64String(_options.SigningKey)),
                SecurityAlgorithms.HmacSha256),
        };

        return new AccessToken(new JsonWebTokenHandler().CreateToken(descriptor), expiresAt);
    }
}
