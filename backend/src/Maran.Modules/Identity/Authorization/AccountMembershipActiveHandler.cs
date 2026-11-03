using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Persistence;
using Maran.Sdk.Contracts;
using Microsoft.AspNetCore.Authorization;

namespace Maran.Modules.Identity.Authorization;

/// <summary>
/// Decides <see cref="AccountMembershipActiveRequirement"/>: a customer token naming an account its
/// login holds no live membership of — suspended from it, or removed from it — is refused on every
/// endpoint that carries the policy.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this is a check on the request and not only on sign-in.</b> The suspension cascade closes
/// the doors that issue tokens (sign-in, refresh, switch) and revokes the sessions speaking for the
/// account, but an access token already issued is stateless and would otherwise keep authenticating
/// for the rest of its fifteen minutes on every endpoint that does not ask the feature gate. This
/// handler reads the membership on each authorized request, so a suspended member is stopped at
/// once whatever endpoint they call. It uses the module's own memberships, so no other module is
/// referenced and nothing is added to the Sdk.
/// </para>
/// <para>
/// <b>A removed member is the same question, answered the same way (issue #340).</b> Removing a team
/// member deletes their membership, but the access token they hold is stateless and keeps
/// authenticating for the rest of its fifteen minutes. Before this handler asked for the row's
/// absence as well as its suspension, only the endpoints that opted into a feature
/// (<c>[RequiresAccountFeature]</c>) noticed, so every other customer endpoint stayed open to a
/// removed member until the token expired. The membership is now read once per request and it must exist and
/// be active.
/// </para>
/// <para>
/// <b>What it deliberately lets through.</b> An unauthenticated caller (the policy's own
/// <c>RequireAuthenticatedUser</c> refuses it); a token whose role is not Customer (an administrator
/// acts for no account); a view-as token, whose own per-request session check ends it the moment the
/// suspension revokes its session, and which the operator may never start against a suspended
/// membership; and a customer token naming no account (its holder acts for none, so there is no
/// membership to ask about).
/// </para>
/// <para>
/// <b>Two refusals, two answers.</b> A suspended membership leaves the requirement UNMET, so the
/// Host's result writer can find it among the pending requirements and answer 409 with the suspension
/// code instead of the generic 403, exactly as it does for the two-factor steering. A missing
/// membership FAILS the requirement explicitly: the person no longer stands in any relation to the
/// account, there is nothing of the account's to name in the answer, and it is the same generic 403
/// a stranger's token would meet (rules/security.md item 6: nothing about the account is confirmed).
/// </para>
/// </remarks>
public sealed class AccountMembershipActiveHandler : AuthorizationHandler<AccountMembershipActiveRequirement>
{
    /// <summary>The module's database context, read for the caller's membership.</summary>
    private readonly IdentityDbContext _dbContext;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The module's database context.</param>
    public AccountMembershipActiveHandler(IdentityDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>
    /// Succeeds only while the caller's membership of the account their token names exists and is
    /// active; leaves the requirement unmet for a suspended one and fails it for a missing one.
    /// </summary>
    /// <param name="context">The authorization context.</param>
    /// <param name="requirement">The requirement being evaluated.</param>
    /// <returns>Resolves once the requirement has been marked succeeded or left unmet.</returns>
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        AccountMembershipActiveRequirement requirement)
    {
        if (!SpeaksForAnAccount(context, out var userId, out var accountId))
        {
            context.Succeed(requirement);
            return;
        }

        var cancellationToken = (context.Resource as HttpContext)?.RequestAborted ?? CancellationToken.None;
        var state = await _dbContext.AccountMemberships
            .AsNoTracking()
            .Where(membership => membership.UserId == userId && membership.AccountId == accountId)
            .Select(membership => (MembershipState?)membership.State)
            .SingleOrDefaultAsync(cancellationToken);

        if (state is null)
        {
            // The row is gone: the member was removed (or the account deleted) while this token was
            // still live. Failed, not merely unmet, so the answer is the generic refusal and not the
            // suspension's.
            context.Fail(new AuthorizationFailureReason(this, "The login holds no membership of the account."));
            return;
        }

        if (state == MembershipState.Active)
        {
            context.Succeed(requirement);
        }
    }

    /// <summary>Reads whether the token is a customer's own, and which login and account it names.</summary>
    /// <param name="context">The authorization context.</param>
    /// <param name="userId">The login the token names, when the answer is true.</param>
    /// <param name="accountId">The account the token names, when the answer is true.</param>
    /// <returns>
    /// <c>true</c> only for an authenticated, non-view-as customer token that carries a readable login
    /// and a readable account; <c>false</c> for every case the type's remarks let through.
    /// </returns>
    private static bool SpeaksForAnAccount(AuthorizationHandlerContext context, out Guid userId, out Guid accountId)
    {
        userId = Guid.Empty;
        accountId = Guid.Empty;
        var user = context.User;

        if (user.Identity?.IsAuthenticated != true
            || !user.HasClaim(PanelClaimTypes.Role, nameof(UserRole.Customer))
            || user.HasClaim(claim =>
            {
                return claim.Type == PanelClaimTypes.Actor || claim.Type == PanelClaimTypes.SessionKind;
            }))
        {
            return false;
        }

        return Guid.TryParse(user.FindFirst(PanelClaimTypes.UserId)?.Value, out userId)
            && Guid.TryParse(user.FindFirst(PanelClaimTypes.AccountId)?.Value, out accountId);
    }
}
