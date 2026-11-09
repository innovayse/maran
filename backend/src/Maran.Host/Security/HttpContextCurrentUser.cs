using System.Security.Claims;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Callers;

namespace Maran.Host.Security;

/// <summary>
/// The caller, read from the current request's validated access token.
/// </summary>
/// <remarks>
/// <para>
/// <b>Six outcomes, and a refusal for everything else.</b> A request with no authenticated
/// principal (or no request at all, as in background work) is an <see cref="AnonymousCaller"/>. A
/// principal authenticated by the API key scheme is an <see cref="ApiKeyCaller"/>, read before any
/// login claim because a key has none; a token that is NOT of that scheme yet carries a key id is
/// refused, so a claim alone can never make a login a key. An
/// authenticated principal whose role claim is exactly <c>Admin</c> is an
/// <see cref="AdministratorCaller"/>; exactly <c>Reseller</c> with a reseller claim, a
/// <see cref="ResellerCaller"/>; exactly <c>Customer</c> with an account claim, a
/// <see cref="CustomerCaller"/>, and without one an <see cref="AccountlessCustomerCaller"/> (the
/// issuer omits the claim for a customer with no memberships, so its absence is a state, not damage;
/// a reseller always has its reseller, so there the absence is damage). An authenticated principal
/// that is none of these — an unknown or missing role, an unreadable user id, an account or reseller
/// claim that is present but not an id, a reseller token with no reseller claim, a view-as marker with
/// no readable operator — is NOT folded into one of them: it raises
/// <see cref="UnrecognisedPrincipalException"/>.
/// </para>
/// <para>
/// <b>Why that refuses and does not degrade.</b> Degrading to anonymous would grant nothing, but it
/// would also report a signed-in person as nobody and hide a broken token issuer behind "not signed
/// in"; degrading to a customer would invent a tenant. The only token this class can see has
/// already passed signature validation, so a principal outside the three roles is a defect to surface,
/// and the request fails closed with a generic 500 that carries no claim value. Previously the same
/// token answered "not an administrator, no account", which is the same denial; the difference is
/// that it is now loud and that no part of the token is half-trusted.
/// </para>
/// </remarks>
public sealed class HttpContextCurrentUser : ICurrentUser
{
    /// <summary>Provides access to the ambient <see cref="HttpContext"/>, when one exists.</summary>
    private readonly IHttpContextAccessor _httpContextAccessor;

    /// <summary>Creates the accessor.</summary>
    /// <param name="httpContextAccessor">Provides access to the ambient <see cref="HttpContext"/>.</param>
    public HttpContextCurrentUser(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    /// <inheritdoc />
    /// <exception cref="UnrecognisedPrincipalException">
    /// The request is authenticated but its token is not an administrator's, a reseller's or a customer's.
    /// </exception>
    public Caller Caller
    {
        get
        {
            var principal = _httpContextAccessor.HttpContext?.User;
            if (principal?.Identity?.IsAuthenticated != true)
            {
                return AnonymousCaller.Instance;
            }

            if (IsApiKeyPrincipal(principal))
            {
                return ReadApiKey(principal);
            }

            if (principal.FindFirst(PanelClaimTypes.ApiKeyId) is not null)
            {
                throw new UnrecognisedPrincipalException("A token that is not an API key carries a key id.");
            }

            if (!Guid.TryParse(principal.FindFirst(PanelClaimTypes.UserId)?.Value, out var userId))
            {
                throw new UnrecognisedPrincipalException("The token carries no readable user id.");
            }

            var username = principal.FindFirst(PanelClaimTypes.Username)?.Value ?? string.Empty;
            var viewedBy = ReadViewingOperator(principal);

            // An exact, case-sensitive match against the three role names. Anything else — an older
            // token, or one carrying a role this build has never heard of — is refused, not demoted.
            var role = principal.FindFirst(PanelClaimTypes.Role)?.Value;
            if (string.Equals(role, nameof(UserRole.Admin), StringComparison.Ordinal))
            {
                return new AdministratorCaller(userId, username, viewedBy);
            }

            if (string.Equals(role, nameof(UserRole.Reseller), StringComparison.Ordinal))
            {
                return ReadReseller(principal, userId, username, viewedBy);
            }

            if (!string.Equals(role, nameof(UserRole.Customer), StringComparison.Ordinal))
            {
                throw new UnrecognisedPrincipalException("The token carries no recognised role.");
            }

            var accountClaim = principal.FindFirst(PanelClaimTypes.AccountId)?.Value;
            if (accountClaim is null)
            {
                return new AccountlessCustomerCaller(userId, username, viewedBy);
            }

            if (!Guid.TryParse(accountClaim, out var accountId))
            {
                throw new UnrecognisedPrincipalException("The customer token carries an unreadable account.");
            }

            return new CustomerCaller(userId, username, accountId, viewedBy);
        }
    }

    /// <summary>Whether the principal was authenticated by the API key scheme, and by nothing else.</summary>
    /// <param name="principal">The validated principal of the request.</param>
    /// <returns>
    /// <c>true</c> when an authenticated identity of the API key scheme is present. The scheme is what
    /// says so, never a claim: a bearer token is signed by this panel's issuer, which never writes a key
    /// id, but the decision does not rest on that.
    /// </returns>
    private static bool IsApiKeyPrincipal(ClaimsPrincipal principal)
    {
        return principal.Identities.Any(identity =>
        {
            return identity.IsAuthenticated
                && string.Equals(identity.AuthenticationType, ApiKeyDefaults.Scheme, StringComparison.Ordinal);
        });
    }

    /// <summary>Reads an API key's caller out of a principal the API key scheme authenticated.</summary>
    /// <param name="principal">The validated principal of the request.</param>
    /// <returns>
    /// The key caller. It carries the scopes the current vocabulary knows and no others, so a stored
    /// scope this build has never heard of grants nothing, and the reseller that owns the key when one does.
    /// </returns>
    /// <exception cref="UnrecognisedPrincipalException">The principal carries no readable key id, or an owner that is not an id.</exception>
    private static ApiKeyCaller ReadApiKey(ClaimsPrincipal principal)
    {
        if (!Guid.TryParse(principal.FindFirst(PanelClaimTypes.ApiKeyId)?.Value, out var keyId))
        {
            throw new UnrecognisedPrincipalException("The API key principal carries no readable key id.");
        }

        var scopes = principal.FindAll(PanelClaimTypes.ApiKeyScope)
            .Select(claim => { return claim.Value; })
            .Where(scope => { return ApiKeyScopes.All.Contains(scope); })
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        // Absent is a key the administrator owns; present but not an id is a damaged principal, which
        // is refused rather than read as "no owner" because that would change what the key may see.
        Guid? resellerId = null;
        if (principal.FindFirst(PanelClaimTypes.ApiKeyReseller) is { } resellerClaim)
        {
            if (!Guid.TryParse(resellerClaim.Value, out var ownerId))
            {
                throw new UnrecognisedPrincipalException("The API key principal carries an unreadable owner.");
            }

            resellerId = ownerId;
        }

        return new ApiKeyCaller(keyId, scopes, resellerId);
    }

    /// <summary>Reads a reseller's caller out of a token whose role is <c>Reseller</c>.</summary>
    /// <param name="principal">The validated principal of the request.</param>
    /// <param name="userId">The login's user id, already read.</param>
    /// <param name="username">The login's name.</param>
    /// <param name="viewedBy">The operator viewing as this reseller, or null.</param>
    /// <returns>The reseller caller.</returns>
    /// <exception cref="UnrecognisedPrincipalException">
    /// The token carries no reseller claim, or one that is not an id. Unlike a customer, a reseller
    /// always acts for one reseller, so an absent claim is a damaged token and not a state: reading it
    /// as a caller with no reseller would invent one that sees nothing, and reading it as anything else
    /// would invent an owner.
    /// </exception>
    private static ResellerCaller ReadReseller(
        ClaimsPrincipal principal,
        Guid userId,
        string username,
        ViewingOperator? viewedBy)
    {
        if (!Guid.TryParse(principal.FindFirst(PanelClaimTypes.ResellerId)?.Value, out var resellerId))
        {
            throw new UnrecognisedPrincipalException("The reseller token carries no readable reseller.");
        }

        return new ResellerCaller(userId, username, resellerId, viewedBy);
    }

    /// <summary>Reads the operator viewing as this login, when the token is a view-as token.</summary>
    /// <param name="principal">The validated principal of the request.</param>
    /// <returns>The operator, or null for an own sign-in.</returns>
    /// <exception cref="UnrecognisedPrincipalException">
    /// The token carries a view-as marker but no readable operator. The bearer handler refuses such a
    /// token before this runs (<see cref="ViewAsSessionValidation"/>); reading it as an own sign-in
    /// here would credit the viewed login with what the operator did, so it is refused again.
    /// </exception>
    private static ViewingOperator? ReadViewingOperator(ClaimsPrincipal principal)
    {
        if (ActorClaimReader.TryRead(principal, out var actor))
        {
            return new ViewingOperator(actor.UserId, actor.Username);
        }

        if (ActorClaimReader.IsViewAs(principal))
        {
            throw new UnrecognisedPrincipalException("The view-as token carries no readable operator.");
        }

        return null;
    }
}
