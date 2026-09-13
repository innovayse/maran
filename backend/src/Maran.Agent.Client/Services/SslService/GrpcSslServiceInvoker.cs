using Maran.Agent.Client.Interfaces;
using Maran.Agent.V1;

namespace Maran.Agent.Client.Services.SslService;

/// <summary>Production <see cref="ISslServiceInvoker"/> backed by the generated gRPC client.</summary>
internal sealed class GrpcSslServiceInvoker : ISslServiceInvoker
{
    /// <summary>The generated gRPC client this adapter wraps.</summary>
    private readonly Maran.Agent.V1.SslService.SslServiceClient _client;

    /// <summary>Wraps <paramref name="client"/> behind the <see cref="ISslServiceInvoker"/> seam.</summary>
    /// <param name="client">The generated client to delegate calls to.</param>
    public GrpcSslServiceInvoker(Maran.Agent.V1.SslService.SslServiceClient client)
    {
        _client = client;
    }

    /// <inheritdoc/>
    public async Task<InstallCertificateResponse> InstallCertificateAsync(
        InstallCertificateRequest request,
        CancellationToken cancellationToken)
    {
        return await _client.InstallCertificateAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<RemoveCertificateResponse> RemoveCertificateAsync(
        RemoveCertificateRequest request,
        CancellationToken cancellationToken)
    {
        return await _client.RemoveCertificateAsync(request, cancellationToken: cancellationToken);
    }
}
