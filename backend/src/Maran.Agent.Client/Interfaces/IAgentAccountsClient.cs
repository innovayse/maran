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
/// exception (rules/csharp.md "Errors: Result, not exceptions").
/// </remarks>
public interface IAgentAccountsClient
{
    /// <summary>Creates the system user, its home directory and its initial quota.</summary>
    /// <param name="username">The account's system user name.</param>
    /// <param name="quotaBytes">The disk quota to apply, in bytes.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>Where the account lives and which uid it got, or a typed failure.</returns>
    Task<Result<CreatedAccountDto>> CreateAsync(string username, ulong quotaBytes, CancellationToken cancellationToken);

    /// <summary>Suspends the account: its password is locked and its shell taken away.</summary>
    /// <param name="username">The account's system user name.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>Success, or a typed failure.</returns>
    Task<Result<bool>> SuspendAsync(string username, CancellationToken cancellationToken);

    /// <summary>Reverses a suspension.</summary>
    /// <param name="username">The account's system user name.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>Success, or a typed failure.</returns>
    Task<Result<bool>> UnsuspendAsync(string username, CancellationToken cancellationToken);

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

    /// <summary>Replaces the account's disk quota.</summary>
    /// <param name="username">The account's system user name.</param>
    /// <param name="quotaBytes">The new quota, in bytes; zero removes the limit.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>Success, or a typed failure.</returns>
    Task<Result<bool>> SetQuotaAsync(string username, ulong quotaBytes, CancellationToken cancellationToken);

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
