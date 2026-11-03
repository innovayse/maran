using System.Diagnostics;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Domain.Policies;
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
    /// <summary>
    /// The value of <see cref="PanelClaimTypes.SessionKind"/> on a view-as token. A closed one-value
    /// vocabulary: an own sign-in carries no such claim, so there is no second value to name.
    /// </summary>
    private const string ViewAsClaimValue = "ViewAs";

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
    /// <remarks>
    /// The token carries the VIEWED login's role, so an administrator viewing as a reseller holds a
    /// <c>Reseller</c> token and every administrator-only endpoint refuses it. Two kinds, one rule
    /// (<see cref="ViewAsKindPolicy.TryDescribe"/>): a customer login is viewed through the session's
    /// active account and the token writes the <c>account</c> claim; a reseller login is viewed
    /// through none and the token writes the <c>reseller</c> claim from the login's own reseller id.
    /// Any disagreement between the session and the login throws.
    /// </remarks>
    public AccessToken IssueViewAs(User viewedUser, Session session, User actor)
    {
        // Refuse rather than sign: a session of the other kind, one belonging to somebody else, or
        // one whose facts do not agree with the login it speaks for reaching here is a caller bug,
        // and a token is the one thing that must not be produced on a bug (issues #317, #179).
        if (session.ViewAs is not { } origin
            || session.UserId != viewedUser.Id
            || origin.ActorUserId != actor.Id
            || !ViewAsKindPolicy.TryDescribe(
                viewedUser.Role,
                viewedUser.ResellerId,
                session.ActiveAccountId,
                out var kind,
                out var subjectId))
        {
            throw new InvalidOperationException("A view-as token was requested for a session that is not a view-as session of these two users.");
        }

        var actorSessionId = origin.ActorSessionId;

        var claims = new Dictionary<string, object>
        {
            [PanelClaimTypes.UserId] = viewedUser.Id.ToString(),
            [PanelClaimTypes.Username] = viewedUser.Username,
            [PanelClaimTypes.Role] = viewedUser.Role.ToString(),
            [PanelClaimTypes.SessionId] = session.Id.ToString(),
            [PanelClaimTypes.SessionKind] = ViewAsClaimValue,

            // RFC 8693 §4.1: the party actually acting, as a JSON object nested in the token. The
            // operator's own session id rides along so the bearer handler can require that the
            // operator is still signed in (ViewAsSessionVerifier).
            [PanelClaimTypes.Actor] = new Dictionary<string, object>
            {
                [PanelClaimTypes.UserId] = actor.Id.ToString(),
                [PanelClaimTypes.Username] = actor.Username,
                [PanelClaimTypes.SessionId] = actorSessionId.ToString(),
            },
        };

        switch (kind)
        {
            case ViewAsKind.Account:
                claims[PanelClaimTypes.AccountId] = subjectId.ToString();
                break;
            case ViewAsKind.Reseller:
                claims[PanelClaimTypes.ResellerId] = subjectId.ToString();
                break;
            default:
                throw new UnreachableException("ViewAsKind has exactly two cases: Account and Reseller.");
        }

        // No forced-two-factor steering: the token speaks for a customer or reseller login, and the
        // steering is the operator's own obligation, already met (or refused) on the operator's own
        // token. The expiry is the SESSION's, so the token and the row it is checked against die together.
        var token = Sign(claims, session.IssuedAt, session.ExpiresAt);
        return new AccessToken(token, session.ExpiresAt, RequiresTwoFactorSetup: false);
    }

    /// <inheritdoc />
    public async Task<AccessToken> IssueAsync(
        User user,
        Guid sessionId,
        Guid? accountId,
        CancellationToken cancellationToken)
    {
        var policy = await _policyCache.GetAsync(cancellationToken);
        var requiresSetup = RequiresTwoFactor(policy, user.Role) && !user.IsTotpEnabled;

        var issuedAt = _clock.UtcNow;
        var expiresAt = issuedAt.AddMinutes(_options.AccessTokenMinutes);

        var claims = new Dictionary<string, object>
        {
            [PanelClaimTypes.UserId] = user.Id.ToString(),
            [PanelClaimTypes.Username] = user.Username,
            [PanelClaimTypes.Role] = user.Role.ToString(),
            [PanelClaimTypes.SessionId] = sessionId.ToString(),
        };

        // The claim names the account the session is acting for, chosen by the caller from the
        // login's memberships. Writing it as an empty string when there is none would make "no
        // account" and "an account whose id failed to render" the same value on the wire, and the
        // authorization code would have to tell them apart.
        if (accountId is { } activeAccountId)
        {
            claims[PanelClaimTypes.AccountId] = activeAccountId.ToString();
        }

        // A reseller login acts for one reseller and for no account. Both halves are refused rather
        // than repaired: a token naming an account beside a reseller, or a reseller role with no
        // reseller, would be a token the host's principal reading refuses on every request.
        if (user.Role == UserRole.Reseller)
        {
            if (accountId is not null || user.ResellerId is not { } resellerId)
            {
                throw new InvalidOperationException("A reseller token needs the login's reseller id and no active account.");
            }

            claims[PanelClaimTypes.ResellerId] = resellerId.ToString();
        }

        // Written only when the steering applies. The claim's PRESENCE is the restriction, so a
        // token from a panel that does not force enrolment carries nothing to misread, and a claim
        // spelled "false" — which every reader would then have to parse correctly — cannot exist.
        if (requiresSetup)
        {
            claims[PanelClaimTypes.TwoFactorSetupRequired] = "true";
        }

        return new AccessToken(Sign(claims, issuedAt, expiresAt), expiresAt, requiresSetup);
    }

    /// <summary>
    /// Reports whether the panel's policy obliges a login of this role to hold a second factor. One
    /// arm per role, so a new role cannot be forgotten into "not required" by a default.
    /// </summary>
    /// <param name="policy">The panel's security policy.</param>
    /// <param name="role">The signing-in login's role.</param>
    /// <returns>True when the role is forced to enrol.</returns>
    private static bool RequiresTwoFactor(SecurityPolicySnapshot policy, UserRole role)
    {
        switch (role)
        {
            case UserRole.Admin:
                return policy.ForceTwoFactorForAdmins;
            case UserRole.Reseller:
                return policy.ForceTwoFactorForResellers;
            case UserRole.Customer:
                return false;
            default:
                throw new UnreachableException("UserRole has exactly three cases: Admin, Customer and Reseller.");
        }
    }

    /// <summary>Signs a set of claims with the panel's key, issuer and audience.</summary>
    /// <param name="claims">The claims to carry.</param>
    /// <param name="issuedAt">The instant the token is issued and becomes valid.</param>
    /// <param name="expiresAt">The instant it stops being accepted.</param>
    /// <returns>The compact-serialized token.</returns>
    private string Sign(Dictionary<string, object> claims, DateTimeOffset issuedAt, DateTimeOffset expiresAt)
    {
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

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }
}
