using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Domain.Policies;
using Maran.Modules.Identity.Persistence;
using Maran.Sdk.Events;

namespace Maran.Modules.Identity.IntegrationEvents.Handlers;

/// <summary>
/// Lets the logins that act for a reactivated account act for it again (<see cref="AccountResuming"/>):
/// every membership of the account becomes <see cref="MembershipState.Active"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>It does not restore sessions, and that is the law rather than an omission.</b>
/// <see cref="AccountSuspendingHandler"/> revoked them, and a revoked session is gone the same way a
/// spent one is — there is nothing here to give back. The owner signs in again and is issued a fresh
/// one, exactly as they would after signing out anywhere else.
/// </para>
/// <para>
/// <b>It acts on the account's memberships</b>, for the reason
/// <see cref="AccountSuspendingHandler"/>'s remarks give: a login that holds other accounts was
/// never blocked as a whole, so there is nothing on the login's lifecycle to lift. The one thing
/// restated on the login is its legacy <c>State</c> / <c>IsAccountSuspended</c> pair, which the
/// previous release reads (<see cref="LegacyOwnerSuspensionPolicy"/>): it is cleared here unless the
/// owner still owns another suspended account. An account with no membership at all has nothing to
/// resume.
/// </para>
/// <para>
/// <b>The failure is not swallowed.</b> Anything thrown here propagates to the Accounts handler,
/// which abandons the reactivation with the account left suspended — the recoverable direction here
/// too, for the same reason every sibling subscriber gives.
/// </para>
/// <para>
/// <b>It writes no audit entry of its own</b>, for the reason <see cref="AccountDeletingHandler"/>
/// gives: the journal's entries record who asked and from where, and a cascade has none of those.
/// </para>
/// </remarks>
public sealed class AccountResumingHandler
{
    /// <summary>The Identity module's database context.</summary>
    private readonly IdentityDbContext _dbContext;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Identity module's database context.</param>
    public AccountResumingHandler(IdentityDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>Resumes the account's memberships, leaving the sessions exactly as the suspension left them.</summary>
    /// <param name="message">The account about to be reactivated.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Resolves once the logins may act for the account again.</returns>
    public async Task HandleAsync(AccountResuming message, CancellationToken cancellationToken)
    {
        var memberships = await _dbContext.AccountMemberships
            .Where(membership => membership.AccountId == message.AccountId)
            .ToListAsync(cancellationToken);
        if (memberships.Count == 0)
        {
            return;
        }

        foreach (var membership in memberships)
        {
            membership.Resume();
        }

        await RestateLegacyColumnsAsync(memberships, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Keeps the owner logins' legacy suspension columns in step with their owner memberships, which
    /// the previous release still reads (<see cref="LegacyOwnerSuspensionPolicy"/>).
    /// </summary>
    /// <param name="memberships">The account's memberships, already changed.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    private async Task RestateLegacyColumnsAsync(
        IReadOnlyCollection<AccountMembership> memberships,
        CancellationToken cancellationToken)
    {
        var ownerIds = memberships
            .Where(membership => membership.Role == MembershipRole.Owner)
            .Select(membership => membership.UserId)
            .ToList();
        if (ownerIds.Count == 0)
        {
            return;
        }

        var owners = await _dbContext.Users
            .Where(user => ownerIds.Contains(user.Id))
            .ToListAsync(cancellationToken);
        var ownerMemberships = await _dbContext.AccountMemberships
            .Where(membership => membership.Role == MembershipRole.Owner && ownerIds.Contains(membership.UserId))
            .ToListAsync(cancellationToken);
        LegacyOwnerSuspensionPolicy.Apply(owners, ownerMemberships);
    }
}
