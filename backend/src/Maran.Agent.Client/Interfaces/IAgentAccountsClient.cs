using Maran.Agent.Client.Services.AccountsService;
using Maran.SharedKernel.Results;

namespace Maran.Agent.Client.Interfaces;

/// <summary>
/// The panel's view of the agent's account operations: the operating-system identity
/// behind a hosting account.
/// </summary>
/// <remarks>
/// Every method returns a <see cref="Result{T}"/> rather than throwing: an account that
/// already exists, or one the agent cannot find, is an answer the caller acts on, not an
/// exception (rules/csharp.md "Errors: Result, not exceptions"). An agent that cannot be reached
/// at all is answered the same way, as <c>AgentUnavailable</c>, by the resilience decorator the
/// Host registers over this contract; the implementation in this project lets the transport's
/// exception out, so that decorator's retry can see it (<see cref="Errors.AgentTransportFailure"/>).
/// </remarks>
public interface IAgentAccountsClient
{
    /// <summary>
    /// Creates the system user, its home directory, its initial quota and the options in
    /// <paramref name="provisioning"/>.
    /// </summary>
    /// <param name="username">The account's system user name.</param>
    /// <param name="quotaBytes">The disk quota to apply, in bytes.</param>
    /// <param name="provisioning">The inode ceiling and login shell to create the user with.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>Where the account lives and which uid it got, or a typed failure.</returns>
    Task<Result<CreatedAccountDto>> CreateAsync(
        string username,
        ulong quotaBytes,
        AccountProvisioningDto provisioning,
        CancellationToken cancellationToken);

    /// <summary>Suspends the account: its password is locked and its shell taken away.</summary>
    /// <param name="username">The account's system user name.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>Success, or a typed failure.</returns>
    Task<Result> SuspendAsync(string username, CancellationToken cancellationToken);

    /// <summary>Reverses a suspension.</summary>
    /// <remarks>
    /// The agent keeps no record of the shell an account had before it was suspended, so the caller
    /// states the shell the account is meant to have once it is active again.
    /// </remarks>
    /// <param name="username">The account's system user name.</param>
    /// <param name="shell">The login shell the account is given back.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>Success, or a typed failure.</returns>
    Task<Result> UnsuspendAsync(string username, AccountLoginShell shell, CancellationToken cancellationToken);

    /// <summary>Reads what the host can be observed to be doing for the account right now.</summary>
    /// <remarks>
    /// Read-only and side-effect free; safe to call on an account in any state. It is the seam that
    /// lets a caller refuse to report a suspension it cannot see: the residue of a suspension is on
    /// the host, so nothing in the panel's own tables can answer this question.
    /// </remarks>
    /// <param name="username">The account's system user name.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>The observed state, or a typed failure.</returns>
    Task<Result<AccountSuspensionStateDto>> GetSuspensionStateAsync(
        string username,
        CancellationToken cancellationToken);

    /// <summary>Removes the system user and everything under its home directory.</summary>
    /// <param name="username">The account's system user name.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>How many bytes were freed, or a typed failure.</returns>
    Task<Result<ulong>> DeleteAsync(string username, CancellationToken cancellationToken);

    /// <summary>Replaces the account's disk quota and its inode quota.</summary>
    /// <remarks>
    /// The quota tool replaces blocks and inodes in one call and the agent keeps no record of the inode
    /// figure it set at creation, so a caller states the account's inode limit on every quota change; a
    /// caller that states none removes the limit.
    /// </remarks>
    /// <param name="username">The account's system user name.</param>
    /// <param name="quotaBytes">The new quota, in bytes; zero removes the limit.</param>
    /// <param name="inodeLimit">The ceiling on files and directories, or <c>null</c> for none.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>Success, or a typed failure.</returns>
    Task<Result> SetQuotaAsync(
        string username,
        ulong quotaBytes,
        ulong? inodeLimit,
        CancellationToken cancellationToken);

    /// <summary>
    /// Holds the account's systemd slice to <paramref name="limits"/>, replacing every ceiling it held
    /// (issue #133).
    /// </summary>
    /// <remarks>
    /// Reaches every process systemd places under the account's slice — its login sessions. The
    /// account's php-fpm workers and, on a host whose cron registers no session, its cron jobs are
    /// outside it; the agent's rpc documentation says so.
    /// </remarks>
    /// <param name="username">The account's system user name.</param>
    /// <param name="limits">The three ceilings, each stated, none left to a default.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>Success, or a typed failure.</returns>
    Task<Result> SetResourceLimitsAsync(
        string username,
        AccountSliceLimitsDto limits,
        CancellationToken cancellationToken);

    /// <summary>Holds the account to a ceiling on open files, or removes the ceiling.</summary>
    /// <remarks>
    /// The ceiling is applied to the account's login sessions through the host's PAM limits. It does
    /// not reach the account's php-fpm workers. Idempotent: stating the same figure twice changes
    /// nothing.
    /// </remarks>
    /// <param name="username">The account's system user name.</param>
    /// <param name="openFiles">The ceiling, or <c>null</c> to remove it.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>Success, or a typed failure.</returns>
    Task<Result> SetOpenFilesLimitAsync(string username, ulong? openFiles, CancellationToken cancellationToken);

    /// <summary>Reads current disk usage and the quota it is measured against.</summary>
    /// <param name="username">The account's system user name.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>The usage, or a typed failure.</returns>
    Task<Result<AccountUsageDto>> GetUsageAsync(string username, CancellationToken cancellationToken);

    /// <summary>
    /// Re-groups every hosting account's home directory to the web server's group where it is not
    /// already that group (issue #28 item E).
    /// </summary>
    /// <remarks>
    /// Host-wide, like <c>RepairGrantsAsync</c>: it takes no account name and acts on every hosting
    /// account on the host at once, because the defect it repairs — a home group left as the account's
    /// own rather than the web server's — is a property of how an account was created, not of any one
    /// caller's account.
    /// </remarks>
    /// <param name="reportOnly">
    /// When true, nothing is changed: the agent classifies every home and reports what it WOULD
    /// re-group.
    /// </param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>The census, or a typed failure.</returns>
    Task<Result<HomeGroupRepairReportDto>> RepairHomeGroupsAsync(bool reportOnly, CancellationToken cancellationToken);
}
