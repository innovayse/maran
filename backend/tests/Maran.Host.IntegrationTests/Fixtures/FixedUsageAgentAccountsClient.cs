using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.AccountsService;
using Maran.SharedKernel.Results;

namespace Maran.Host.IntegrationTests.Fixtures;

/// <summary>
/// An <see cref="IAgentAccountsClient"/> that reports every account as using a fixed number of bytes
/// and records the quotas it was told to set, so a plan edit that moves a disk quota can be driven
/// over real HTTP with no agent on the machine. Everything else a plan edit never reaches throws, so
/// a handler that strayed into it fails loudly instead of passing against a silent success.
/// </summary>
public sealed class FixedUsageAgentAccountsClient : IAgentAccountsClient
{
    /// <summary>The bytes every account is reported as using.</summary>
    private readonly ulong _usedBytes;

    /// <summary>Creates the client.</summary>
    /// <param name="usedBytes">The bytes every account is reported as using.</param>
    public FixedUsageAgentAccountsClient(ulong usedBytes)
    {
        _usedBytes = usedBytes;
    }

    /// <summary>Every quota set so far, as <c>name:bytes</c>, in order.</summary>
    public List<string> QuotasSet { get; } = [];

    /// <inheritdoc />
    public Task<Result<AccountUsageDto>> GetUsageAsync(string username, CancellationToken cancellationToken)
    {
        return Task.FromResult(Result<AccountUsageDto>.Ok(
            new AccountUsageDto(_usedBytes, 0, AccountQuotaState.Enforced, QuotaUnenforceableReason.Unspecified)));
    }

    /// <inheritdoc />
    public Task<Result<bool>> SetQuotaAsync(string username, ulong quotaBytes, CancellationToken cancellationToken)
    {
        QuotasSet.Add($"{username}:{quotaBytes}");
        return Task.FromResult(Result<bool>.Ok(true));
    }

    /// <inheritdoc />
    public Task<Result<CreatedAccountDto>> CreateAsync(
        string username,
        ulong quotaBytes,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException();
    }

    /// <inheritdoc />
    public Task<Result<bool>> SuspendAsync(string username, CancellationToken cancellationToken)
    {
        throw new NotSupportedException();
    }

    /// <inheritdoc />
    public Task<Result<bool>> UnsuspendAsync(string username, CancellationToken cancellationToken)
    {
        throw new NotSupportedException();
    }

    /// <inheritdoc />
    public Task<Result<AccountSuspensionStateDto>> GetSuspensionStateAsync(
        string username,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException();
    }

    /// <inheritdoc />
    public Task<Result<ulong>> DeleteAsync(string username, CancellationToken cancellationToken)
    {
        throw new NotSupportedException();
    }

    /// <inheritdoc />
    public Task<Result<HomeGroupRepairReportDto>> RepairHomeGroupsAsync(
        bool reportOnly,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException();
    }
}
