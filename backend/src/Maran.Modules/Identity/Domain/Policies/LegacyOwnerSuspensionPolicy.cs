using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;

namespace Maran.Modules.Identity.Domain.Policies;

/// <summary>
/// Restates the suspension of the accounts a login owns into the legacy <c>State</c> /
/// <c>IsAccountSuspended</c> pair of its row, which the previous release reads to refuse the owner
/// of a suspended account (rules/architecture.md, expand then contract).
/// </summary>
/// <remarks>
/// This release reads a suspension from the membership alone, so this policy is the one place the
/// login row is told about it, for the sake of a rollback and no other reason. A login that owns
/// several accounts is restated as suspended while ANY of them is: the previous release could
/// represent one account per login, and a rollback that fails open on a suspension is the worse way
/// to be wrong. Deleted with the legacy columns in the contract release.
/// </remarks>
public static class LegacyOwnerSuspensionPolicy
{
    /// <summary>Restates each owner's legacy columns from the owner memberships it holds.</summary>
    /// <param name="owners">The logins that own at least one of the accounts concerned.</param>
    /// <param name="ownerMemberships">Every owner membership of those logins, whatever its account.</param>
    public static void Apply(IEnumerable<User> owners, IReadOnlyCollection<AccountMembership> ownerMemberships)
    {
        foreach (var owner in owners)
        {
            var anySuspended = ownerMemberships.Any(
                membership => membership.UserId == owner.Id && membership.State == MembershipState.Suspended);
            owner.SyncLegacyColumns(anySuspended ? MembershipState.Suspended : MembershipState.Active);
        }
    }
}
