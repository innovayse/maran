using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.AccountsService;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Accounts.Tests.TestSupport;

/// <summary>
/// An agent double that reports SUCCESS from every call and then answers the attestation with a
/// login that is still unlocked.
/// </summary>
/// <remarks>
/// <para>
/// It is a separate type rather than a flag on <see cref="RecordingAgentAccountsClient"/> because it
/// models a different machine: one whose <c>usermod</c> returned zero without the passwd entry
/// changing. That is exactly the shape the attestation exists for — every call answered success and
/// the account is not stopped — and a double that could not produce it would leave the refusal
/// untested no matter how many other tests passed.
/// </para>
/// <para>
/// Only the two methods a suspension reaches are implemented; the rest throw, so a test that starts
/// using this double for something else is told rather than quietly answered.
/// </para>
/// </remarks>
public sealed class UnlockedAgentAccountsClient : IAgentAccountsClient
{
    /// <summary>Reports success without the host having locked anything.</summary>
    /// <param name="username">Unused.</param>
    /// <param name="cancellationToken">Unused.</param>
    /// <returns>Success.</returns>
    public Task<Result<bool>> SuspendAsync(string username, CancellationToken cancellationToken)
    {
        return Task.FromResult(Result<bool>.Ok(true));
    }

    /// <summary>Answers with a readable directory, no vhosts, and a login that is not locked.</summary>
    /// <param name="username">Unused.</param>
    /// <param name="cancellationToken">Unused.</param>
    /// <returns>The unlocked observation.</returns>
    public Task<Result<AccountSuspensionStateDto>> GetSuspensionStateAsync(
        string username,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(Result<AccountSuspensionStateDto>.Ok(
            new AccountSuspensionStateDto(false, AccountLoginPasswordState.Usable, true, [], 0, 0, 0, [])));
    }

    /// <summary>Not exercised by these tests; a call would be a test asking the wrong question.</summary>
    /// <param name="username">Unused.</param>
    /// <param name="quotaBytes">Unused.</param>
    /// <param name="cancellationToken">Unused.</param>
    /// <returns>Never returns.</returns>
    public Task<Result<CreatedAccountDto>> CreateAsync(
        string username,
        ulong quotaBytes,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException();
    }

    /// <summary>Not exercised by these tests.</summary>
    /// <param name="username">Unused.</param>
    /// <param name="cancellationToken">Unused.</param>
    /// <returns>Never returns.</returns>
    public Task<Result<bool>> UnsuspendAsync(string username, CancellationToken cancellationToken)
    {
        throw new NotSupportedException();
    }

    /// <summary>Not exercised by these tests.</summary>
    /// <param name="username">Unused.</param>
    /// <param name="cancellationToken">Unused.</param>
    /// <returns>Never returns.</returns>
    public Task<Result<ulong>> DeleteAsync(string username, CancellationToken cancellationToken)
    {
        throw new NotSupportedException();
    }

    /// <summary>Not exercised by these tests.</summary>
    /// <param name="username">Unused.</param>
    /// <param name="quotaBytes">Unused.</param>
    /// <param name="cancellationToken">Unused.</param>
    /// <returns>Never returns.</returns>
    public Task<Result<bool>> SetQuotaAsync(string username, ulong quotaBytes, CancellationToken cancellationToken)
    {
        throw new NotSupportedException();
    }

    /// <summary>Not exercised by these tests.</summary>
    /// <param name="username">Unused.</param>
    /// <param name="cancellationToken">Unused.</param>
    /// <returns>Never returns.</returns>
    public Task<Result<AccountUsageDto>> GetUsageAsync(string username, CancellationToken cancellationToken)
    {
        throw new NotSupportedException();
    }

    /// <summary>Not exercised by these tests.</summary>
    /// <param name="reportOnly">Unused.</param>
    /// <param name="cancellationToken">Unused.</param>
    /// <returns>Never returns.</returns>
    public Task<Result<HomeGroupRepairReportDto>> RepairHomeGroupsAsync(bool reportOnly, CancellationToken cancellationToken)
    {
        throw new NotSupportedException();
    }
}
