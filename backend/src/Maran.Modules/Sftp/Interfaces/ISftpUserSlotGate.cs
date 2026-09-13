using Maran.Modules.Sftp.Domain.Entities;

namespace Maran.Modules.Sftp.Interfaces;

/// <summary>
/// The last word on whether an account may hold one more SFTP login, taken at the moment the row is
/// written rather than before the host is touched.
/// </summary>
/// <remarks>
/// <para>
/// This exists because the plan limit is a statement about a ROW COUNT and no key can express one. A
/// unique index refuses a duplicate value; "at most N rows for this account" is not a value, so the
/// count and the insert are two steps and something has to make them one. That something is this
/// seam, and the reason it is a seam rather than four lines inside the handler is that its only
/// honest implementation speaks PostgreSQL — an advisory lock and an explicit transaction — while
/// most of the handler's tests run on a provider that has neither.
/// </para>
/// <para>
/// A caller reaches this only AFTER the agent has made the login, so a refusal here means a login
/// exists on the host that no row will own: the caller compensates. That cost is why the handler
/// still checks the limit before calling the agent at all — the ordinary over-limit request is
/// refused without the host being touched, and this gate is what closes the narrow window in which
/// two requests both passed that check.
/// </para>
/// <para>
/// The shape is deliberately the one the Ftp module settled on rather than a second one. The two
/// allowances are different numbers over different daemons, but the defect is the same defect and the
/// argument for each rejected alternative is the same argument, so a reader who has understood one
/// has understood both.
/// </para>
/// </remarks>
public interface ISftpUserSlotGate
{
    /// <summary>Writes the row if, and only if, the account is still inside its allowance.</summary>
    /// <param name="sftpUser">The row to write, for a login the agent has already created.</param>
    /// <param name="allowance">How many logins the account's plan allows in total.</param>
    /// <param name="cancellationToken">Cancels the claim.</param>
    /// <returns>
    /// True when the row was written and committed; false when a concurrent creation had already
    /// taken the last slot, in which case nothing was written.
    /// </returns>
    /// <remarks>
    /// A database failure is not answered with false: it is thrown, because the caller's answer to a
    /// duplicate key and to a lost connection are different and only one of them compensates on the
    /// host. An implementation that swallowed either would make the caller delete a login whose
    /// credential the winner has already been shown.
    /// </remarks>
    Task<bool> TryTakeAsync(SftpUser sftpUser, int allowance, CancellationToken cancellationToken);
}
