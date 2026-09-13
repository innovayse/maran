using System.Runtime.CompilerServices;
using Grpc.Core;
using Maran.Agent.Client.Interfaces;
using Maran.Agent.V1;

namespace Maran.Agent.Client.Services.PhpService;

/// <summary>Production <see cref="IPhpServiceInvoker"/> backed by the generated gRPC client.</summary>
internal sealed class GrpcPhpServiceInvoker : IPhpServiceInvoker
{
    /// <summary>The generated gRPC client this adapter wraps.</summary>
    private readonly Maran.Agent.V1.PhpService.PhpServiceClient _client;

    /// <summary>Wraps <paramref name="client"/> behind the <see cref="IPhpServiceInvoker"/> seam.</summary>
    /// <param name="client">The generated client to delegate calls to.</param>
    public GrpcPhpServiceInvoker(Maran.Agent.V1.PhpService.PhpServiceClient client)
    {
        _client = client;
    }

    /// <inheritdoc/>
    public async Task<ListPhpVersionsResponse> ListPhpVersionsAsync(
        ListPhpVersionsRequest request,
        CancellationToken cancellationToken)
    {
        return await _client.ListPhpVersionsAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<InstallPhpVersionResponse> InstallPhpVersionAsync(
        InstallPhpVersionRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var call = _client.InstallPhpVersion(request, cancellationToken: cancellationToken);

        await foreach (var response in call.ResponseStream.ReadAllAsync(cancellationToken))
        {
            yield return response;
        }
    }
}
