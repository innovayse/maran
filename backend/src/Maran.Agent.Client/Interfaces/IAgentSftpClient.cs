using Maran.Agent.Client.Services.SftpService;
using Maran.SharedKernel.Results;
using Maran.SharedKernel.Security;

namespace Maran.Agent.Client.Interfaces;

/// <summary>
/// The panel's view of the agent's SFTP logins: an OpenSSH login, a real system account in one
/// group, chrooted into a root-owned jail with the account's real home bind-mounted inside it.
/// </summary>
/// <remarks>
/// <b>SFTP is no longer the only transfer daemon, and this contract's name is honest about which
/// one it drives.</b> Since FTPS shipped, <see cref="IAgentFtpsClient"/> stands beside this one for
/// the vsftpd this panel installs; the two answer about different daemons out of different jails.
/// The one member here that is NOT SFTP-only is
/// <see cref="SetAccountLoginsLockedAsync"/>, and its own documentation says so rather than leaving
/// the reader to infer it from the interface's name.
///
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

    /// <summary>Locks, or unlocks, every file-transfer login one account holds, of either daemon.</summary>
    /// <param name="accountUsername">System username of the account whose every login is affected.</param>
    /// <param name="locked"><c>true</c> to lock every login, <c>false</c> to unlock them.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>
    /// How many sessions the lock direction ended, or a typed failure. An account with no login is a
    /// success. <see cref="AccountLoginLockOutcomeDto.SessionsEnded"/> is <c>null</c> when the host's
    /// answer carried no count and on the unlock direction, which ends no session — a caller that
    /// renders that as zero states a completeness claim the host never made.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>Why this call has to exist.</b> <c>usermod --lock &lt;account&gt;</c> does not reach these
    /// logins. Each is its own passwd entry named <c>&lt;account&gt;_&lt;name&gt;</c> created with
    /// <c>useradd --non-unique --uid &lt;account uid&gt;</c> so that it writes as the account, so
    /// locking the account's own entry leaves every one of them authenticating — a suspended
    /// customer kept a working WRITE credential into their home.
    /// </para>
    /// <para>
    /// It takes an ACCOUNT and no login name: the logins come from the host's own password database
    /// rather than from this module's rows, because a table can only describe what the panel
    /// remembers creating. Nothing is deleted and no password is changed, so the resume gives back
    /// the credential the customer already has.
    /// </para>
    /// <para>
    /// <b>It is the one member of this contract that is not SFTP-only.</b> The agent serves it out of
    /// <c>ops::logins</c> rather than <c>ops::sftp</c>, so every file-transfer login the account
    /// holds is turned here whichever daemon serves it — an enumeration owned by the SFTP area would
    /// walk past an FTPS login and report success. The rpc is declared on the agent's
    /// <c>SftpService</c> and so reaches the panel through this client; the interface it sits on is
    /// therefore narrower than the operation, and this paragraph exists so that a reader who came
    /// here through the name does not conclude the FTPS credentials were left open.
    /// </para>
    /// </remarks>
    /// <para>
    /// <b>On the locking direction it also ends the sessions the account already has open</b>, and
    /// that is why this member answers with a value rather than a <c>bool</c>. The lock marker is
    /// consulted at AUTHENTICATION, so it refuses the customer's next login and does nothing to a
    /// transfer already running; the cull is what makes a suspension stop access rather than only
    /// stop new access. It is a privileged action with a cost to the customer — a transfer is cut at
    /// whatever byte it had reached and the partial file stays — so the count has to reach the panel:
    /// the operator is warned about that cost BEFORE a suspension, and an attestation that could not
    /// state the outcome left the promise with nothing after it.
    /// </para>
    Task<Result<AccountLoginLockOutcomeDto>> SetAccountLoginsLockedAsync(
        string accountUsername,
        bool locked,
        CancellationToken cancellationToken);
}
