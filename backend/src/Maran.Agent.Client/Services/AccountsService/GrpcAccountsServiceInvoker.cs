using Maran.Agent.Client.Interfaces;
using Maran.Agent.V1;

namespace Maran.Agent.Client.Services.AccountsService;

/// <summary>Production <see cref="IAccountsServiceInvoker"/> backed by the generated gRPC client.</summary>
internal sealed class GrpcAccountsServiceInvoker : IAccountsServiceInvoker
{
    /// <summary>The generated gRPC client this adapter wraps.</summary>
    private readonly Maran.Agent.V1.AccountsService.AccountsServiceClient _client;

    /// <summary>Wraps <paramref name="client"/> behind the <see cref="IAccountsServiceInvoker"/> seam.</summary>
    /// <param name="client">The generated client to delegate calls to.</param>
    public GrpcAccountsServiceInvoker(Maran.Agent.V1.AccountsService.AccountsServiceClient client)
    {
        _client = client;
    }

    /// <inheritdoc/>
    public async Task<CreateAccountResponse> CreateAccountAsync(
        CreateAccountRequest request,
        CancellationToken cancellationToken)
    {
        return await _client.CreateAccountAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<SuspendAccountResponse> SuspendAccountAsync(
        SuspendAccountRequest request,
        CancellationToken cancellationToken)
    {
        return await _client.SuspendAccountAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<UnsuspendAccountResponse> UnsuspendAccountAsync(
        UnsuspendAccountRequest request,
        CancellationToken cancellationToken)
    {
        return await _client.UnsuspendAccountAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<DeleteAccountResponse> DeleteAccountAsync(
        DeleteAccountRequest request,
        CancellationToken cancellationToken)
    {
        return await _client.DeleteAccountAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<SetAccountQuotaResponse> SetAccountQuotaAsync(
        SetAccountQuotaRequest request,
        CancellationToken cancellationToken)
    {
        return await _client.SetAccountQuotaAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<GetAccountUsageResponse> GetAccountUsageAsync(
        GetAccountUsageRequest request,
        CancellationToken cancellationToken)
    {
        return await _client.GetAccountUsageAsync(request, cancellationToken: cancellationToken);
    }
}
