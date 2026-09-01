using Maran.Agent.Client.Interfaces;
using Maran.Agent.V1;

namespace Maran.Agent.Client.Tests.Services.SslService;

/// <summary>Stub of <c>ISslServiceInvoker</c> returning a canned response.</summary>
internal sealed class StubSslService : ISslServiceInvoker
{
    /// <summary>Response returned from <see cref="InstallCertificateAsync"/>.</summary>
    public InstallCertificateResponse InstallResponse { get; set; } = new();

    /// <summary>The last request the stub received, for asserting the mapping.</summary>
    public InstallCertificateRequest? LastRequest { get; private set; }

    /// <summary>Response returned from <see cref="RemoveCertificateAsync"/>.</summary>
    public RemoveCertificateResponse RemoveResponse { get; set; } = new();

    /// <summary>The last removal request the stub received, for asserting the mapping.</summary>
    public RemoveCertificateRequest? LastRemoveRequest { get; private set; }

    /// <inheritdoc/>
    public Task<InstallCertificateResponse> InstallCertificateAsync(
        InstallCertificateRequest request,
        CancellationToken cancellationToken)
    {
        LastRequest = request;
        return Task.FromResult(InstallResponse);
    }

    /// <inheritdoc/>
    public Task<RemoveCertificateResponse> RemoveCertificateAsync(
        RemoveCertificateRequest request,
        CancellationToken cancellationToken)
    {
        LastRemoveRequest = request;
        return Task.FromResult(RemoveResponse);
    }
}
