using Maran.Agent.Client.Interfaces;
using Maran.Agent.V1;

namespace Maran.Agent.Client.Services.SystemService;

/// <summary>Production <see cref="ISystemServiceInvoker"/> backed by the generated gRPC client.</summary>
internal sealed class GrpcSystemServiceInvoker : ISystemServiceInvoker
{
    /// <summary>The generated gRPC client this adapter wraps.</summary>
    private readonly Maran.Agent.V1.SystemService.SystemServiceClient _client;

    /// <summary>Wraps <paramref name="client"/> behind the <see cref="ISystemServiceInvoker"/> seam.</summary>
    /// <param name="client">The generated client to delegate calls to.</param>
    public GrpcSystemServiceInvoker(Maran.Agent.V1.SystemService.SystemServiceClient client)
    {
        _client = client;
    }

    /// <inheritdoc/>
    public async Task<GetAgentInfoResponse> GetAgentInfoAsync(CancellationToken ct) =>
        await _client.GetAgentInfoAsync(new GetAgentInfoRequest(), cancellationToken: ct);
}
