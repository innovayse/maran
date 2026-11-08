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

    /// <summary>What each creation asked the agent to provision beyond the name and the quota, in order.</summary>
    public List<AccountProvisioningDto> Provisioned { get; } = [];

    /// <summary>The open-files ceilings the panel asked the agent to set, by account, in order.</summary>
    public List<(string Username, ulong? OpenFiles)> OpenFilesLimits { get; } = [];

    /// <inheritdoc />
    public Task<Result<CreatedAccountDto>> CreateAsync(
        string username,
        ulong quotaBytes,
        AccountProvisioningDto provisioning,
        CancellationToken cancellationToken)
    {
        Created.Add(username);
        Provisioned.Add(provisioning);

        return Task.FromResult(Result<CreatedAccountDto>.Ok(new CreatedAccountDto(Home, FirstUid)));
    }

    /// <inheritdoc />
    public Task<Result> SuspendAsync(string username, CancellationToken cancellationToken)
    {
        throw new NotSupportedException();
    }

    /// <inheritdoc />
    public Task<Result> UnsuspendAsync(string username, AccountLoginShell shell, CancellationToken cancellationToken)
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

    /// <summary>Succeeds without recording: no test that uses this double is about runtime ceilings.</summary>
    /// <param name="username">Unused.</param>
    /// <param name="limits">Unused.</param>
    /// <param name="cancellationToken">Unused.</param>
    /// <returns>Success.</returns>
    public Task<Result> SetResourceLimitsAsync(
        string username,
        AccountSliceLimitsDto limits,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(Result.Ok());
    }

    /// <inheritdoc />
    public Task<Result> SetQuotaAsync(
        string username,
        ulong quotaBytes,
        ulong? inodeLimit,
        CancellationToken cancellationToken)
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

    /// <summary>Records the ceiling and succeeds.</summary>
    /// <param name="username">The account's system user name.</param>
    /// <param name="openFiles">The ceiling asked for, or <c>null</c> for none.</param>
    /// <param name="cancellationToken">Unused.</param>
    /// <returns>Success.</returns>
    public Task<Result> SetOpenFilesLimitAsync(string username, ulong? openFiles, CancellationToken cancellationToken)
    {
        OpenFilesLimits.Add((username, openFiles));

        return Task.FromResult(Result.Ok());
    }
}
