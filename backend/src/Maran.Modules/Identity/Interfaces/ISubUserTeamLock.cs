using Microsoft.EntityFrameworkCore.Storage;

namespace Maran.Modules.Identity.Interfaces;

/// <summary>
/// Opens the transaction an invitation is decided and written in, and makes it the only one for its
/// account at that moment, so the team-size check and the insert behind it cannot be interleaved by
/// a second invitation.
/// </summary>
/// <remarks>
/// <para>
/// This is a seam rather than three lines in the handler because its only honest implementation speaks
/// PostgreSQL (a row lock inside an explicit transaction), while most of the handler's tests run on a
/// provider that has neither. The plan for what the unit tests can and cannot say about it is on
/// their stand-in; the property itself is measured against real PostgreSQL by
/// <c>Maran.Host.IntegrationTests.SubUserInviteCapRaceTests</c>.
/// </para>
/// <para>
/// <b>Why a lock and not a unique index or a serializable transaction.</b> The cap is a cardinality
/// ("at most N members of this account") and no key expresses one. <c>READ COMMITTED</c> gives each
/// statement its own snapshot, so two invitations that both counted N-1 both insert. A serializable
/// transaction would detect it but reports it as <c>40001</c> at commit, after the work is done, and
/// would force a retry loop around a handler that must answer every request identically. The lock
/// makes the second invitation WAIT, and its count then sees the first one's committed row.
/// </para>
/// </remarks>
public interface ISubUserTeamLock
{
    /// <summary>Begins a transaction and takes the account's invitation lock inside it.</summary>
    /// <param name="accountId">The hosting account whose team is being added to.</param>
    /// <param name="cancellationToken">Cancels the wait for the lock.</param>
    /// <returns>
    /// The open transaction. The lock is held until it is committed or rolled back (disposing it rolls
    /// it back), and it covers every statement the caller runs on the module's context meanwhile.
    /// </returns>
    Task<IDbContextTransaction> BeginAsync(Guid accountId, CancellationToken cancellationToken);
}
