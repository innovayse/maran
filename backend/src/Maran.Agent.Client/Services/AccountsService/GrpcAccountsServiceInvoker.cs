using Maran.Agent.Client.Interfaces;
using Maran.Agent.V1;

namespace Maran.Agent.Client.Services.AccountsService;

/// <summary>Production <see cref="IAccountsServiceInvoker"/> backed by the generated gRPC client.</summary>
internal sealed class GrpcAccountsServiceInvoker : IAccountsServiceInvoker
{
    /// <summary>Where the channel for the server the current call is aimed at comes from.</summary>
    private readonly IAgentChannels _channels;

    /// <summary>Wraps <paramref name="channels"/> behind the <see cref="IAccountsServiceInvoker"/> seam.</summary>
    /// <param name="channels">Hands out the channel of the server each call is aimed at.</param>
    public GrpcAccountsServiceInvoker(IAgentChannels channels)
    {
        _channels = channels;
    }

    /// <summary>
    /// A generated client over the channel of the server the current call is aimed at, built per use.
    /// Building it is cheap (it holds the channel and nothing else), and building it per call is what
    /// lets one invoker serve every server: the target is read when the call starts, never earlier.
    /// </summary>
    private Maran.Agent.V1.AccountsService.AccountsServiceClient Client
    {
        get { return new Maran.Agent.V1.AccountsService.AccountsServiceClient(_channels.ForCurrent()); }
    }

    /// <inheritdoc/>
    public async Task<CreateAccountResponse> CreateAccountAsync(
        CreateAccountRequest request,
        CancellationToken cancellationToken)
    {
        return await Client.CreateAccountAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<SuspendAccountResponse> SuspendAccountAsync(
        SuspendAccountRequest request,
        CancellationToken cancellationToken)
    {
        return await Client.SuspendAccountAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<UnsuspendAccountResponse> UnsuspendAccountAsync(
        UnsuspendAccountRequest request,
        CancellationToken cancellationToken)
    {
        return await Client.UnsuspendAccountAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<GetAccountSuspensionStateResponse> GetAccountSuspensionStateAsync(
        GetAccountSuspensionStateRequest request,
        CancellationToken cancellationToken)
    {
        return await Client.GetAccountSuspensionStateAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<DeleteAccountResponse> DeleteAccountAsync(
        DeleteAccountRequest request,
        CancellationToken cancellationToken)
    {
        return await Client.DeleteAccountAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<SetAccountQuotaResponse> SetAccountQuotaAsync(
        SetAccountQuotaRequest request,
        CancellationToken cancellationToken)
    {
        return await Client.SetAccountQuotaAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<SetAccountResourceLimitsResponse> SetAccountResourceLimitsAsync(
        SetAccountResourceLimitsRequest request,
        CancellationToken cancellationToken)
    {
        return await Client.SetAccountResourceLimitsAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<SetAccountOpenFilesLimitResponse> SetAccountOpenFilesLimitAsync(
        SetAccountOpenFilesLimitRequest request,
        CancellationToken cancellationToken)
    {
        return await Client.SetAccountOpenFilesLimitAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<GetAccountUsageResponse> GetAccountUsageAsync(
        GetAccountUsageRequest request,
        CancellationToken cancellationToken)
    {
        return await Client.GetAccountUsageAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<RepairAccountHomeGroupsResponse> RepairAccountHomeGroupsAsync(
        RepairAccountHomeGroupsRequest request,
        CancellationToken cancellationToken)
    {
        return await Client.RepairAccountHomeGroupsAsync(request, cancellationToken: cancellationToken);
    }
}
