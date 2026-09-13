using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.SitesService;
using Maran.Agent.Client.Services.SslService;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Ssl.Tests.TestSupport;

/// <summary>
/// An <see cref="IAgentSslClient"/> double recording what the panel asked the agent to do, and
/// answering with a prepared success or a prepared typed failure.
/// </summary>
/// <remarks>
/// It records the SiteDescriptor of every call, because half of what this module does is decide what
/// the rewritten vhost should say. A descriptor with <c>HasCertificate</c> false on an install would
/// switch TLS on and immediately render a vhost that does not serve it, and nothing about the
/// returned expiry would show that.
/// </remarks>
public sealed class RecordingAgentSslClient : IAgentSslClient
{
    /// <summary>The refusal to answer with, or null to succeed.</summary>
    private readonly Error? _failure;

    /// <summary>When a successful install reports the certificate expires.</summary>
    private readonly DateTimeOffset _expiresAt;

    /// <summary>The account, domain, material and descriptor of every install, in order.</summary>
    public List<InstalledMaterial> Installs { get; } = [];

    /// <summary>The account, domain and descriptor of every removal, in order.</summary>
    public List<(string Account, string Domain, SiteDescriptor Site)> Removals { get; } = [];

    /// <summary>Creates a client that succeeds, or one that always refuses.</summary>
    /// <param name="failure">The refusal to answer with, or null to succeed.</param>
    /// <param name="expiresAt">When a successful install reports the certificate expires.</param>
    public RecordingAgentSslClient(Error? failure = null, DateTimeOffset? expiresAt = null)
    {
        _failure = failure;
        _expiresAt = expiresAt ?? new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
    }

    /// <inheritdoc />
    public Task<Result<InstalledCertificateDto>> InstallCertificateAsync(
        string accountUsername,
        string domain,
        string certificatePem,
        string privateKeyPem,
        SiteDescriptor site,
        CancellationToken cancellationToken)
    {
        Installs.Add(new InstalledMaterial(accountUsername, domain, certificatePem, privateKeyPem, site));

        return Task.FromResult(_failure is null
            ? Result<InstalledCertificateDto>.Ok(new InstalledCertificateDto(_expiresAt))
            : Result<InstalledCertificateDto>.Fail(_failure));
    }

    /// <inheritdoc />
    public Task<Result<bool>> RemoveCertificateAsync(
        string accountUsername,
        string domain,
        SiteDescriptor site,
        CancellationToken cancellationToken)
    {
        Removals.Add((accountUsername, domain, site));

        return Task.FromResult(_failure is null ? Result<bool>.Ok(true) : Result<bool>.Fail(_failure));
    }
}
