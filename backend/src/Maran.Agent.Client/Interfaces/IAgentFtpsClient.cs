using Maran.Agent.Client.Services.FtpsService;
using Maran.SharedKernel.Results;
using Maran.SharedKernel.Security;

namespace Maran.Agent.Client.Interfaces;

/// <summary>
/// The panel's view of this server's own FTPS daemon: the switch that configures and runs it, the
/// observation it answers with, and the logins it authorises.
/// </summary>
/// <remarks>
/// <para>
/// A contract of its own beside <see cref="IAgentSftpClient"/> rather than more methods on it,
/// because the two answer about different daemons: the SFTP half drives an OpenSSH the operator
/// already runs, and this half owns a vsftpd this panel installed, configured and starts. Splitting
/// them is also what makes the capability meaningful — a module that manages FTPS declares
/// <c>AgentCapability.Ftps</c> and gains nothing over SSH.
/// </para>
/// <para>
/// There is no chroot path anywhere in this contract and there must never be one: the jail is
/// derived from the validated account name and created root-owned by the agent, so a request cannot
/// name the directory it will be confined to. The entire chroot-escape class is gone by
/// construction rather than by a containment check that has to be right every time.
/// </para>
/// <para>
/// <b>This client never creates certificate material.</b> <see cref="IAgentSslClient"/> owns that
/// store, its pairing check and its self-signed marker; a second writer of that directory is how a
/// customer's private key gets destroyed. The daemon calls here only READ whether material exists
/// for a hostname, and <see cref="ReloadTlsAsync"/> only restarts a daemon so it picks up material
/// something else replaced.
/// </para>
/// <para>
/// <b>What a failure can and cannot tell the panel today.</b> Every refusal arrives as one of the
/// contract's <c>ErrorCode</c> values, translated by <c>AgentErrorTranslator</c> into a code and a
/// kind. The contract has no code for "this account is busy": the agent's per-account lock never
/// waits, it refuses, and that refusal reaches the panel as <c>AgentSystemFailure</c> — a fault, an
/// outage-shaped sentence — for a condition whose honest answer is "try again in a moment". Adding
/// a wire code for it is the owner's decision and is recorded as an open question; nothing here
/// works around it by reading the agent's English sentence, because that sentence is
/// operator-facing text this panel logs and never branches on.
/// </para>
/// </remarks>
public interface IAgentFtpsClient
{
    /// <summary>Renders, validates and applies the FTPS daemon's configuration, then brings it up.</summary>
    /// <param name="hostname">The hostname the daemon serves, and the name its certificate material is filed under.</param>
    /// <param name="passivePortMin">Lowest port of the passive data range, inclusive.</param>
    /// <param name="passivePortMax">Highest port of the passive data range, inclusive.</param>
    /// <param name="passiveAddress">
    /// The address to advertise in the PASV reply for a host behind NAT. Empty means the key is not
    /// written at all and the daemon answers with the address the control connection arrived on,
    /// which is the ordinary host.
    /// </param>
    /// <param name="maxClients">The daemon's concurrent-session ceiling.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>
    /// What the agent observed after applying the configuration, or a typed failure —
    /// <c>AgentNotFound</c> when no certificate material exists for <paramref name="hostname"/>,
    /// <c>AgentValidationFailed</c> when the daemon refused the rendered file and the previous
    /// state was restored.
    /// </returns>
    /// <remarks>
    /// The range and the ceiling are the PANEL's numbers, never the agent's: the agent must not own
    /// a product default, and the range has to be the same one the panel opened in the firewall.
    /// Idempotent — an unchanged configuration writes nothing and does not restart a running daemon,
    /// which matters because a nightly reconcile that re-applied the same file would bounce every
    /// live session on the server once a day.
    /// </remarks>
    Task<Result<FtpsStatusDto>> EnableAsync(
        string hostname,
        uint passivePortMin,
        uint passivePortMax,
        string passiveAddress,
        uint maxClients,
        CancellationToken cancellationToken);

    /// <summary>Stops the daemon and takes it out of the boot sequence.</summary>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>What the agent observed after stopping it, or a typed failure.</returns>
    /// <remarks>
    /// Logins are NOT removed: disabling a service is not revoking credentials, and an operator who
    /// re-enables it expects the same customers to be able to log in. Idempotent.
    /// </remarks>
    Task<Result<FtpsStatusDto>> DisableAsync(CancellationToken cancellationToken);

    /// <summary>Reports what the daemon is doing, measured rather than recalled.</summary>
    /// <param name="hostname">
    /// The hostname whose certificate material the answer describes. EMPTY means "report the daemon
    /// and range facts only", so a panel with no hostname persisted yet still gets an answer instead
    /// of an error — and the three certificate fields then carry their absent values because nothing
    /// was asked about, which a caller must not read as "no material exists".
    /// </param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>The nine facts, or a typed failure.</returns>
    Task<Result<FtpsStatusDto>> GetStatusAsync(string hostname, CancellationToken cancellationToken);

    /// <summary>Restarts the daemon so it picks up certificate material replaced underneath it.</summary>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>What the agent observed afterwards, or a typed failure.</returns>
    /// <remarks>
    /// vsftpd loads its certificate once, at start, so this is a RESTART and it aborts transfers in
    /// flight. The panel calls it only when the SSL module reports new material for the FTPS
    /// hostname. A daemon that is not running is left alone and reported as it is.
    /// </remarks>
    Task<Result<FtpsStatusDto>> ReloadTlsAsync(CancellationToken cancellationToken);

    /// <summary>Creates an FTPS login for an account, and its jail if that is not there yet.</summary>
    /// <param name="arguments">
    /// The account, the customer's suffix and the minted password, carried together in a type whose
    /// generated <c>ToString()</c> cannot print the password.
    /// </param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>
    /// The fully-qualified login name as created, or a typed failure — <c>AgentAlreadyExists</c> for
    /// a login that is already there, whose password is deliberately NOT changed, so that retrying a
    /// creation whose response was lost cannot reset the credential the customer was already shown.
    /// </returns>
    Task<Result<string>> CreateUserAsync(
        CreateFtpsUserArguments arguments,
        CancellationToken cancellationToken);

    /// <summary>Sets an existing login's password; the only thing about an FTPS login that is settable.</summary>
    /// <param name="accountUsername">
    /// System username of the owning account. Load-bearing rather than informational: it is what the
    /// agent checks the login's jail against before it writes anything, because
    /// <c>&lt;account&gt;_&lt;name&gt;</c> has no unique decomposition when account names may carry
    /// the separator — so without it a request authorised for one tenant could re-credential
    /// another tenant's login.
    /// </param>
    /// <param name="ftpsUsername">Login name suffix, namespaced under the account exactly as at creation.</param>
    /// <param name="password">
    /// The new password. There is no "leave unchanged" here — setting the password is the whole
    /// operation, and an empty value is refused by the agent as invalid input rather than treated as
    /// a no-op, because a silent no-op would report success for a credential that was never rotated.
    /// </param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>Success, or a typed failure — <c>AgentNotFound</c> when there is no such login.</returns>
    Task<Result<bool>> SetPasswordAsync(
        string accountUsername,
        string ftpsUsername,
        SensitiveString password,
        CancellationToken cancellationToken);

    /// <summary>Removes an FTPS login, and only the login.</summary>
    /// <param name="accountUsername">System username of the owning account.</param>
    /// <param name="ftpsUsername">Login name suffix, namespaced under the account as at creation.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>Success, or a typed failure — <c>AgentNotFound</c> when there is no such login.</returns>
    /// <remarks>
    /// The account's files are untouched: the login's passwd home is the jail, the account's real
    /// home is bind-mounted inside it, and removing a login means revoking a key rather than
    /// deleting what it opened. The jail stays for the account's other logins.
    /// </remarks>
    Task<Result<bool>> DeleteUserAsync(
        string accountUsername,
        string ftpsUsername,
        CancellationToken cancellationToken);
}
