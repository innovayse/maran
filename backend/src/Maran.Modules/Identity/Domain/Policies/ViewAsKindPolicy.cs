using System.Diagnostics;
using Maran.Modules.Identity.Domain.Enums;

namespace Maran.Modules.Identity.Domain.Policies;

/// <summary>
/// The stateless rule that says which logins can be viewed as, of what kind, and what a view-as
/// session of that kind must look like (issues #317, #179).
/// </summary>
/// <remarks>
/// One hop only: the allowed edges are administrator to reseller, administrator to account and
/// reseller to its own account. An administrator's login is never a target, so no token can lead to
/// administrator power by way of a view-as. The consistency rule here is asked by the token issuer
/// before it signs and by the session verifier on every request, so the two cannot drift apart.
/// </remarks>
public static class ViewAsKindPolicy
{
    /// <summary>Names the kind of view-as a login's role allows, when it allows one.</summary>
    /// <param name="role">The viewed login's role.</param>
    /// <param name="kind">The kind, set only when the method returns true.</param>
    /// <returns>True when a login of this role may be viewed as; false for an administrator.</returns>
    public static bool TryKindOf(UserRole role, out ViewAsKind kind)
    {
        switch (role)
        {
            case UserRole.Customer:
                kind = ViewAsKind.Account;
                return true;
            case UserRole.Reseller:
                kind = ViewAsKind.Reseller;
                return true;
            case UserRole.Admin:
                kind = default;
                return false;
            default:
                throw new UnreachableException("UserRole has exactly three cases: Admin, Customer and Reseller.");
        }
    }

    /// <summary>
    /// Reads what a view-as session looks at from the facts it is made of, and refuses facts that do
    /// not describe one of the two allowed kinds consistently: an account view has an active account;
    /// a reseller view has none and the login carries a reseller id.
    /// </summary>
    /// <param name="role">The viewed login's role.</param>
    /// <param name="resellerId">The viewed login's reseller id, null for a login that has none.</param>
    /// <param name="activeAccountId">The session's active account, null when it has none.</param>
    /// <param name="kind">The kind, set only when the method returns true.</param>
    /// <param name="subjectId">The account's or the reseller's id as <paramref name="kind"/> says, set only when the method returns true.</param>
    /// <returns>True when the facts describe one of the two allowed kinds consistently.</returns>
    public static bool TryDescribe(
        UserRole role,
        Guid? resellerId,
        Guid? activeAccountId,
        out ViewAsKind kind,
        out Guid subjectId)
    {
        subjectId = Guid.Empty;
        if (!TryKindOf(role, out kind))
        {
            return false;
        }

        switch (kind)
        {
            case ViewAsKind.Account when activeAccountId is { } accountId:
                subjectId = accountId;
                return true;
            case ViewAsKind.Reseller when activeAccountId is null && resellerId is { } viewedResellerId:
                subjectId = viewedResellerId;
                return true;
            case ViewAsKind.Account:
            case ViewAsKind.Reseller:
                return false;
            default:
                throw new UnreachableException("ViewAsKind has exactly two cases: Account and Reseller.");
        }
    }
}
