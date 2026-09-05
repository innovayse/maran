using Maran.Agent.Client.Interfaces;
using Maran.Agent.V1;

namespace Maran.Agent.Client.Services.SftpService;

/// <summary>Production <see cref="ISftpServiceInvoker"/> backed by the generated gRPC client.</summary>
internal sealed class GrpcSftpServiceInvoker : ISftpServiceInvoker
{
    /// <summary>The generated gRPC client this adapter wraps.</summary>
    private readonly Maran.Agent.V1.SftpService.SftpServiceClient _client;

    /// <summary>Wraps <paramref name="client"/> behind the <see cref="ISftpServiceInvoker"/> seam.</summary>
    /// <param name="client">The generated client to delegate calls to.</param>
    public GrpcSftpServiceInvoker(Maran.Agent.V1.SftpService.SftpServiceClient client)
    {
        _client = client;
    }

    /// <inheritdoc/>
    public async Task<CreateSftpUserResponse> CreateSftpUserAsync(
        CreateSftpUserRequest request,
        CancellationToken cancellationToken)
    {
        return await _client.CreateSftpUserAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<SetSftpPasswordResponse> SetSftpPasswordAsync(
        SetSftpPasswordRequest request,
        CancellationToken cancellationToken)
    {
        return await _client.SetSftpPasswordAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<DeleteSftpUserResponse> DeleteSftpUserAsync(
        DeleteSftpUserRequest request,
        CancellationToken cancellationToken)
    {
        return await _client.DeleteSftpUserAsync(request, cancellationToken: cancellationToken);
    }
}
