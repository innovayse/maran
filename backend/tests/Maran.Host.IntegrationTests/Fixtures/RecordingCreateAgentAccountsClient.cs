using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.AccountsService;
using Maran.SharedKernel.Results;

namespace Maran.Host.IntegrationTests.Fixtures;

/// <summary>
/// An <see cref="IAgentAccountsClient"/> that records the account names the panel asked the agent to
/// create and answers every creation with success, so the name rules of <c>POST /api/v1/accounts</c>
/// can be driven over real HTTP with no agent on the machine. The point of the record is the negative:
/// a refused name must never reach the agent. Everything a creation never reaches throws, so a handler
/// that strayed into it fails loudly instead of passing against a silent success.
/// </summary>
public sealed class RecordingCreateAgentAccountsClient : IAgentAccountsClient
{
    /// <summary>The home directory every creation reports.</summary>
    private const string Home = "/home/created";

    /// <summary>The user id every creation reports.</summary>
    private const uint FirstUid = 1001;

    /// <summary>The account names the panel asked the agent to create, in order.</summary>
    public List<string> Created { get; } = [];

    /// <inheritdoc />
    public Task<Result<CreatedAccountDto>> CreateAsync(
        string username,
        ulong quotaBytes,
        CancellationToken cancellationToken)
    {
        Created.Add(username);

        return Task.FromResult(Result<CreatedAccountDto>.Ok(new CreatedAccountDto(Home, FirstUid)));
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
    public Task<Result<bool>> SetQuotaAsync(string username, ulong quotaBytes, CancellationToken cancellationToken)
    {
        throw new NotSupportedException();
    }

    /// <inheritdoc />
    public Task<Result<AccountUsageDto>> GetUsageAsync(string username, CancellationToken cancellationToken)
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
