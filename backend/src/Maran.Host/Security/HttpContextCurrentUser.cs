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
/// <b>Three outcomes, and a refusal for everything else.</b> A request with no authenticated
/// principal (or no request at all, as in background work) is an <see cref="AnonymousCaller"/>. An
/// authenticated principal whose role claim is exactly <c>Admin</c> is an
/// <see cref="AdministratorCaller"/>; exactly <c>Customer</c> with an account claim, a
/// <see cref="CustomerCaller"/>, and without one an <see cref="AccountlessCustomerCaller"/> (the
/// issuer omits the claim for a customer with no memberships, so its absence is a state, not damage).
/// An authenticated principal that is none of these — an unknown or missing role, an unreadable user
/// id, an account claim that is present but not an id, a view-as marker with no readable operator —
/// is NOT folded into one of them: it raises <see cref="UnrecognisedPrincipalException"/>.
/// </para>
/// <para>
/// <b>Why that refuses and does not degrade.</b> Degrading to anonymous would grant nothing, but it
/// would also report a signed-in person as nobody and hide a broken token issuer behind "not signed
/// in"; degrading to a customer would invent a tenant. The only token this class can see has
/// already passed signature validation, so a principal outside the two roles is a defect to surface,
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
    /// The request is authenticated but its token is not an administrator's or a customer's.
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

            if (!Guid.TryParse(principal.FindFirst(PanelClaimTypes.UserId)?.Value, out var userId))
            {
                throw new UnrecognisedPrincipalException("The token carries no readable user id.");
            }

            var username = principal.FindFirst(PanelClaimTypes.Username)?.Value ?? string.Empty;
            var viewedBy = ReadViewingOperator(principal);

            // An exact, case-sensitive match against the two role names. Anything else — an older
            // token, or one carrying a role this build has never heard of — is refused, not demoted.
            var role = principal.FindFirst(PanelClaimTypes.Role)?.Value;
            if (string.Equals(role, nameof(UserRole.Admin), StringComparison.Ordinal))
            {
                return new AdministratorCaller(userId, username, viewedBy);
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
