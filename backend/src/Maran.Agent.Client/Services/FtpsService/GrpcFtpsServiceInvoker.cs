using Maran.Agent.Client.Interfaces;
using Maran.Agent.V1;

namespace Maran.Agent.Client.Services.FtpsService;

/// <summary>Production <see cref="IFtpsServiceInvoker"/> backed by the generated gRPC client.</summary>
internal sealed class GrpcFtpsServiceInvoker : IFtpsServiceInvoker
{
    /// <summary>The generated gRPC client this adapter wraps.</summary>
    private readonly Maran.Agent.V1.FtpsService.FtpsServiceClient _client;

    /// <summary>Wraps <paramref name="client"/> behind the <see cref="IFtpsServiceInvoker"/> seam.</summary>
    /// <param name="client">The generated client to delegate calls to.</param>
    public GrpcFtpsServiceInvoker(Maran.Agent.V1.FtpsService.FtpsServiceClient client)
    {
        _client = client;
    }

    /// <inheritdoc/>
    public async Task<EnableFtpsResponse> EnableFtpsAsync(
        EnableFtpsRequest request,
        CancellationToken cancellationToken)
    {
        return await _client.EnableFtpsAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<DisableFtpsResponse> DisableFtpsAsync(
        DisableFtpsRequest request,
        CancellationToken cancellationToken)
    {
        return await _client.DisableFtpsAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<GetFtpsStatusResponse> GetFtpsStatusAsync(
        GetFtpsStatusRequest request,
        CancellationToken cancellationToken)
    {
        return await _client.GetFtpsStatusAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<ReloadFtpsTlsResponse> ReloadFtpsTlsAsync(
        ReloadFtpsTlsRequest request,
        CancellationToken cancellationToken)
    {
        return await _client.ReloadFtpsTlsAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<CreateFtpsUserResponse> CreateFtpsUserAsync(
        CreateFtpsUserRequest request,
        CancellationToken cancellationToken)
    {
        return await _client.CreateFtpsUserAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<SetFtpsPasswordResponse> SetFtpsPasswordAsync(
        SetFtpsPasswordRequest request,
        CancellationToken cancellationToken)
    {
        return await _client.SetFtpsPasswordAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<DeleteFtpsUserResponse> DeleteFtpsUserAsync(
        DeleteFtpsUserRequest request,
        CancellationToken cancellationToken)
    {
        return await _client.DeleteFtpsUserAsync(request, cancellationToken: cancellationToken);
    }
}
