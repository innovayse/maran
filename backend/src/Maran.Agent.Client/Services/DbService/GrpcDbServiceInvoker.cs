using Maran.Agent.Client.Interfaces;
using Maran.Agent.V1;

namespace Maran.Agent.Client.Services.DbService;

/// <summary>Production <see cref="IDbServiceInvoker"/> backed by the generated gRPC client.</summary>
internal sealed class GrpcDbServiceInvoker : IDbServiceInvoker
{
    /// <summary>The generated gRPC client this adapter wraps.</summary>
    private readonly Maran.Agent.V1.DbService.DbServiceClient _client;

    /// <summary>Wraps <paramref name="client"/> behind the <see cref="IDbServiceInvoker"/> seam.</summary>
    /// <param name="client">The generated client to delegate calls to.</param>
    public GrpcDbServiceInvoker(Maran.Agent.V1.DbService.DbServiceClient client)
    {
        _client = client;
    }

    /// <inheritdoc/>
    public async Task<CreateDatabaseResponse> CreateDatabaseAsync(
        CreateDatabaseRequest request,
        CancellationToken cancellationToken)
    {
        return await _client.CreateDatabaseAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<DropDatabaseResponse> DropDatabaseAsync(
        DropDatabaseRequest request,
        CancellationToken cancellationToken)
    {
        return await _client.DropDatabaseAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<SetDatabasePasswordResponse> SetDatabasePasswordAsync(
        SetDatabasePasswordRequest request,
        CancellationToken cancellationToken)
    {
        return await _client.SetDatabasePasswordAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<ListDatabasesResponse> ListDatabasesAsync(
        ListDatabasesRequest request,
        CancellationToken cancellationToken)
    {
        return await _client.ListDatabasesAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<GetDatabaseSizeResponse> GetDatabaseSizeAsync(
        GetDatabaseSizeRequest request,
        CancellationToken cancellationToken)
    {
        return await _client.GetDatabaseSizeAsync(request, cancellationToken: cancellationToken);
    }
}
