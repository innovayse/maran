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

    /// <summary>Response returned from <see cref="InstallPanelCertificateAsync"/>.</summary>
    public InstallPanelCertificateResponse InstallPanelResponse { get; set; } = new();

    /// <summary>The last panel install request the stub received.</summary>
    public InstallPanelCertificateRequest? LastPanelRequest { get; private set; }

    /// <summary>Response returned from <see cref="WritePanelChallengeAsync"/>.</summary>
    public WritePanelChallengeResponse WriteChallengeResponse { get; set; } = new();

    /// <summary>The last panel challenge write the stub received.</summary>
    public WritePanelChallengeRequest? LastChallengeRequest { get; private set; }

    /// <summary>Response returned from <see cref="RemovePanelChallengeAsync"/>.</summary>
    public RemovePanelChallengeResponse RemoveChallengeResponse { get; set; } = new();

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

    /// <inheritdoc/>
    public Task<InstallPanelCertificateResponse> InstallPanelCertificateAsync(
        InstallPanelCertificateRequest request,
        CancellationToken cancellationToken)
    {
        LastPanelRequest = request;
        return Task.FromResult(InstallPanelResponse);
    }

    /// <inheritdoc/>
    public Task<WritePanelChallengeResponse> WritePanelChallengeAsync(
        WritePanelChallengeRequest request,
        CancellationToken cancellationToken)
    {
        LastChallengeRequest = request;
        return Task.FromResult(WriteChallengeResponse);
    }

    /// <inheritdoc/>
    public Task<RemovePanelChallengeResponse> RemovePanelChallengeAsync(
        RemovePanelChallengeRequest request,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(RemoveChallengeResponse);
    }
}
