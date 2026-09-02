using Maran.SharedKernel.Results;
using Maran.SharedKernel.Security;

namespace Maran.Agent.Client.Interfaces;

/// <summary>
/// The panel's view of the agent's file-transfer logins. This panel ships SFTP and nothing else: an
/// OpenSSH login, a real system account in one group, chrooted into a root-owned jail with the
/// account's real home bind-mounted inside it.
/// </summary>
/// <remarks>
/// There is no chroot path anywhere in this contract, and there must never be one. The jail is
/// derived from the validated account name and created root-owned by the agent, so the entire
/// chroot-escape class of bug is gone by construction rather than by a containment check that has to
/// be right every time.
///
/// A login has exactly one settable thing — its password — because everything else about it (home,
/// jail, shell, group) is derived from the account rather than chosen by the caller. That is why
/// there is a <see cref="SetPasswordAsync"/> and no general "update".
/// </remarks>
public interface IAgentSftpClient
{
    /// <summary>Creates an SFTP login for an account, and its jail if that is not there yet.</summary>
    /// <param name="accountUsername">System username of the owning account; the jail and the mount are derived from it.</param>
    /// <param name="sftpUsername">Login name suffix chosen by the customer; the agent namespaces it under the account.</param>
    /// <param name="password">
    /// The password the panel just minted. Carried in a non-printing wrapper and stripped from the
    /// agent's own error text before that text is logged.
    /// </param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>
    /// The fully-qualified login name as created, or a typed failure — <c>AgentAlreadyExists</c> for
    /// a login that is already there, whose password is deliberately NOT changed, so that retrying a
    /// creation whose response was lost cannot reset the credential the customer was already shown.
    /// </returns>
    Task<Result<string>> CreateAsync(
        string accountUsername,
        string sftpUsername,
        SensitiveString password,
        CancellationToken cancellationToken);

    /// <summary>Sets an existing login's password; the only way to change one.</summary>
    /// <param name="accountUsername">System username of the owning account.</param>
    /// <param name="sftpUsername">Login name suffix, namespaced under the account exactly as at creation.</param>
    /// <param name="password">
    /// The new password. There is no "leave unchanged" here — setting the password is the whole
    /// operation, and an empty value is refused by the agent as invalid input rather than treated as
    /// a no-op.
    /// </param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>Success, or a typed failure — <c>AgentNotFound</c> when there is no such login.</returns>
    Task<Result<bool>> SetPasswordAsync(
        string accountUsername,
        string sftpUsername,
        SensitiveString password,
        CancellationToken cancellationToken);

    /// <summary>Removes an SFTP login, and only the login.</summary>
    /// <param name="accountUsername">System username of the owning account.</param>
    /// <param name="sftpUsername">Login name suffix, namespaced under the account as at creation.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>Success, or a typed failure — <c>AgentNotFound</c> when there is no such login.</returns>
    /// <remarks>
    /// The account's files are NOT touched: the login's passwd home is the jail, the account's real
    /// home is bind-mounted inside it, and removing a login means revoking a key rather than
    /// deleting what it opened.
    /// </remarks>
    Task<Result<bool>> DeleteAsync(
        string accountUsername,
        string sftpUsername,
        CancellationToken cancellationToken);
}
