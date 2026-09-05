using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Domain.ValueObjects;
using Maran.Modules.Identity.Interfaces;
using Maran.Modules.Identity.Options;
using Maran.Sdk.Contracts;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Maran.Modules.Identity.Services;

/// <summary>Issues HMAC-SHA256 access tokens carrying the claims the panel authorizes against.</summary>
/// <remarks>
/// It is also the single place the forced-two-factor steering is decided. A token whose holder must
/// still enrol carries <see cref="PanelClaimTypes.TwoFactorSetupRequired"/>, and
/// <c>TwoFactorEnrolmentCompleteHandler</c> refuses every endpoint that is not part of enrolment
/// while it is present. Deciding it here rather than at each call site is what makes the refresh
/// endpoint safe: a refresh re-issues a token for the same user, so it re-evaluates the policy
/// rather than inheriting or dropping a flag somebody remembered to pass.
/// </remarks>
public sealed class JwtAccessTokenIssuer : IAccessTokenIssuer
{
    /// <summary>Signing key, issuer, audience and lifetime.</summary>
    private readonly JwtOptions _options;

    /// <summary>The panel's security policy, read for the forced-two-factor decision.</summary>
    private readonly SecurityPolicyCache _policyCache;

    /// <summary>The panel's clock; never <c>DateTime.UtcNow</c> (rules/csharp.md "Forbidden").</summary>
    private readonly IClock _clock;

    /// <summary>Creates the issuer.</summary>
    /// <param name="options">The bound and validated <see cref="JwtOptions"/>.</param>
    /// <param name="policyCache">The panel's security policy.</param>
    /// <param name="clock">The panel's clock, so tests can decide what "now" means.</param>
    public JwtAccessTokenIssuer(IOptions<JwtOptions> options, SecurityPolicyCache policyCache, IClock clock)
    {
        _options = options.Value;
        _policyCache = policyCache;
        _clock = clock;
    }

    /// <inheritdoc />
    public async Task<AccessToken> IssueAsync(User user, Guid sessionId, CancellationToken cancellationToken)
    {
        var policy = await _policyCache.GetAsync(cancellationToken);
        var requiresSetup = policy.ForceTwoFactorForAdmins
            && user.Role == UserRole.Admin
            && !user.IsTotpEnabled;

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

        // Written only when the steering applies. The claim's PRESENCE is the restriction, so a
        // token from a panel that does not force enrolment carries nothing to misread, and a claim
        // spelled "false" — which every reader would then have to parse correctly — cannot exist.
        if (requiresSetup)
        {
            claims[PanelClaimTypes.TwoFactorSetupRequired] = "true";
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

        return new AccessToken(new JsonWebTokenHandler().CreateToken(descriptor), expiresAt, requiresSetup);
    }
}
