using Maran.Agent.Client.Interfaces;
using Maran.Agent.V1;

namespace Maran.Agent.Client.Services.CronService;

/// <summary>Production <see cref="ICronServiceInvoker"/> backed by the generated gRPC client.</summary>
internal sealed class GrpcCronServiceInvoker : ICronServiceInvoker
{
    /// <summary>The generated gRPC client this adapter wraps.</summary>
    private readonly Maran.Agent.V1.CronService.CronServiceClient _client;

    /// <summary>Wraps <paramref name="client"/> behind the <see cref="ICronServiceInvoker"/> seam.</summary>
    /// <param name="client">The generated client to delegate calls to.</param>
    public GrpcCronServiceInvoker(Maran.Agent.V1.CronService.CronServiceClient client)
    {
        _client = client;
    }

    /// <inheritdoc/>
    public async Task<ListCronEntriesResponse> ListCronEntriesAsync(
        ListCronEntriesRequest request,
        CancellationToken cancellationToken)
    {
        return await _client.ListCronEntriesAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<CreateCronEntryResponse> CreateCronEntryAsync(
        CreateCronEntryRequest request,
        CancellationToken cancellationToken)
    {
        return await _client.CreateCronEntryAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<UpdateCronEntryResponse> UpdateCronEntryAsync(
        UpdateCronEntryRequest request,
        CancellationToken cancellationToken)
    {
        return await _client.UpdateCronEntryAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<DeleteCronEntryResponse> DeleteCronEntryAsync(
        DeleteCronEntryRequest request,
        CancellationToken cancellationToken)
    {
        return await _client.DeleteCronEntryAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<SetCronEntryEnabledResponse> SetCronEntryEnabledAsync(
        SetCronEntryEnabledRequest request,
        CancellationToken cancellationToken)
    {
        return await _client.SetCronEntryEnabledAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<GetCronEntryOutputResponse> GetCronEntryOutputAsync(
        GetCronEntryOutputRequest request,
        CancellationToken cancellationToken)
    {
        return await _client.GetCronEntryOutputAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<GetCronEnvironmentResponse> GetCronEnvironmentAsync(
        GetCronEnvironmentRequest request,
        CancellationToken cancellationToken)
    {
        return await _client.GetCronEnvironmentAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<SetCronEnvironmentResponse> SetCronEnvironmentAsync(
        SetCronEnvironmentRequest request,
        CancellationToken cancellationToken)
    {
        return await _client.SetCronEnvironmentAsync(request, cancellationToken: cancellationToken);
    }
}
