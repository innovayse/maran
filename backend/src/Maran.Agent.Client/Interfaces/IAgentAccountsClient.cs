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
}
