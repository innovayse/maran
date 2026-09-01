using System.Net.Sockets;
using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.SitesService;
using Maran.Agent.Client.Services.SslService;
using Maran.SharedKernel.Results;

namespace Maran.Host.Tests.Resilience;

/// <summary>An inner TLS client that records its arguments and can fail on demand.</summary>
internal sealed class RecordingAgentSslClient : IAgentSslClient
{
    /// <summary>How many calls fail with a transport error before one succeeds.</summary>
    public int FailuresBeforeSuccess { get; set; }

    /// <summary>How many times the install method was entered.</summary>
    public int Calls { get; private set; }

    /// <summary>The account username of the last call.</summary>
    public string? LastAccountUsername { get; private set; }

    /// <summary>The domain of the last call.</summary>
    public string? LastDomain { get; private set; }

    /// <summary>The certificate material of the last call.</summary>
    public string? LastCertificatePem { get; private set; }

    /// <summary>The key material of the last call.</summary>
    public string? LastPrivateKeyPem { get; private set; }

    /// <summary>The site descriptor of the last call.</summary>
    public SiteDescriptor? LastSite { get; private set; }

    /// <inheritdoc/>
    public async Task<Result<InstalledCertificateDto>> InstallCertificateAsync(
        string accountUsername,
        string domain,
        string certificatePem,
        string privateKeyPem,
        SiteDescriptor site,
        CancellationToken cancellationToken)
    {
        LastAccountUsername = accountUsername;
        LastDomain = domain;
        LastCertificatePem = certificatePem;
        LastPrivateKeyPem = privateKeyPem;
        LastSite = site;
        Calls++;

        if (Calls <= FailuresBeforeSuccess)
        {
            throw new SocketException((int)SocketError.ConnectionRefused);
        }

        await Task.Yield();

        return Result<InstalledCertificateDto>.Ok(new InstalledCertificateDto(DateTimeOffset.UnixEpoch));
    }

    /// <inheritdoc/>
    public async Task<Result<bool>> RemoveCertificateAsync(
        string accountUsername,
        string domain,
        SiteDescriptor site,
        CancellationToken cancellationToken)
    {
        LastAccountUsername = accountUsername;
        LastDomain = domain;
        LastSite = site;
        Calls++;

        if (Calls <= FailuresBeforeSuccess)
        {
            throw new SocketException((int)SocketError.ConnectionRefused);
        }

        await Task.Yield();

        return Result<bool>.Ok(true);
    }
}
