using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.SitesService;
using Maran.Agent.Client.Services.SslService;
using Maran.Agent.Simulated.Enums;
using Maran.Agent.Simulated.Errors;
using Maran.Agent.Simulated.State;
using Maran.Agent.Simulated.Validation;

namespace Maran.Agent.Simulated.Services.SslService;

/// <summary>
/// The placing of TLS material on the server and the wiring of it into a site, kept in memory: the
/// certificate is parsed to learn when it expires and then forgotten, and nothing is written, served
/// or reloaded.
/// </summary>
/// <remarks>
/// <para>
/// A certificate is installed for a site that exists and belongs to the named account, so a missing
/// site is <c>AgentNotFound</c> as on a real host; removing a certificate that was never installed is
/// <c>AgentNotFound</c> too. Installing again replaces the record, which is how a renewal arrives.
/// </para>
/// <para>
/// The panel's own certificate and the challenge files for its host name are single facts of the
/// host. A challenge is a bounded table, because a visitor can ask for one at will.
/// </para>
/// </remarks>
public sealed class SimulatedAgentSslClient : IAgentSslClient
{
    /// <summary>How long a generated placeholder certificate is valid.</summary>
    private static readonly TimeSpan SelfSignedLifetime = TimeSpan.FromDays(365);

    /// <summary>The simulated host's state.</summary>
    private readonly SimulatedAgentLedger _ledger;

    /// <summary>The source of the current moment.</summary>
    private readonly IClock _clock;

    /// <summary>Creates a client over <paramref name="ledger"/>.</summary>
    /// <param name="ledger">The simulated host's state.</param>
    /// <param name="clock">The source of the moment a certificate was put in place.</param>
    public SimulatedAgentSslClient(SimulatedAgentLedger ledger, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(clock);

        _ledger = ledger;
        _clock = clock;
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
        cancellationToken.ThrowIfCancellationRequested();

        var limits = _ledger.Limits;
        var wellFormed = SimulatedInput.IsAccountName(accountUsername, limits.MaxAccountNameLength)
            && SimulatedInput.IsDomain(domain, limits.MaxDomainLength)
            && IsWithinSizeCaps(certificatePem, privateKeyPem);
        if (!wellFormed)
        {
            return Task.FromResult(Result<InstalledCertificateDto>.Fail(SimulatedAgentErrors.InvalidInput()));
        }

        var expiry = SimulatedCertificateReader.ReadExpiry(certificatePem, privateKeyPem);
        if (!expiry.IsSuccess)
        {
            return Task.FromResult(Result<InstalledCertificateDto>.Fail(expiry.Error!));
        }

        var result = _ledger.Atomically(
            () =>
            {
                var ownsSite = _ledger.Vhosts.TryGet(domain, out var vhost)
                    && string.Equals(vhost.AccountUsername, accountUsername, StringComparison.Ordinal);
                if (!ownsSite)
                {
                    return Result<InstalledCertificateDto>.Fail(SimulatedAgentErrors.NotFound());
                }

                var certificate = new SimulatedCertificate(accountUsername, domain, expiry.Value, _clock.UtcNow);
                if (!_ledger.Certificates.Replace(domain, _ => { return certificate; })
                    && _ledger.Certificates.Add(domain, certificate) == SimulatedLedgerAdd.LimitReached)
                {
                    return Result<InstalledCertificateDto>.Fail(SimulatedAgentErrors.LimitReached());
                }

                return Result<InstalledCertificateDto>.Ok(new InstalledCertificateDto(expiry.Value));
            });

        return Task.FromResult(result);
    }

    /// <inheritdoc />
    public Task<Result> RemoveCertificateAsync(
        string accountUsername,
        string domain,
        SiteDescriptor site,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var result = _ledger.Atomically(
            () =>
            {
                var owned = _ledger.Certificates.TryGet(domain, out var certificate)
                    && string.Equals(certificate.AccountUsername, accountUsername, StringComparison.Ordinal);
                if (!owned)
                {
                    return Result.Fail(SimulatedAgentErrors.NotFound());
                }

                _ledger.Certificates.Remove(domain);

                return Result.Ok();
            });

        return Task.FromResult(result);
    }

    /// <inheritdoc />
    public Task<Result<InstalledCertificateDto>> InstallPanelCertificateAsync(
        string certificatePem,
        string privateKeyPem,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!IsWithinSizeCaps(certificatePem, privateKeyPem))
        {
            return Task.FromResult(Result<InstalledCertificateDto>.Fail(SimulatedAgentErrors.InvalidInput()));
        }

        var expiry = SimulatedCertificateReader.ReadExpiry(certificatePem, privateKeyPem);
        if (!expiry.IsSuccess)
        {
            return Task.FromResult(Result<InstalledCertificateDto>.Fail(expiry.Error!));
        }

        _ledger.PanelCertificate.Replace(new SimulatedPanelCertificate(expiry.Value, false, _clock.UtcNow));

        return Task.FromResult(Result<InstalledCertificateDto>.Ok(new InstalledCertificateDto(expiry.Value)));
    }

    /// <inheritdoc />
    public Task<Result<InstalledCertificateDto>> RestorePanelSelfSignedAsync(
        string hostName,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!SimulatedInput.IsDomain(hostName, _ledger.Limits.MaxDomainLength))
        {
            return Task.FromResult(Result<InstalledCertificateDto>.Fail(SimulatedAgentErrors.InvalidInput()));
        }

        var now = _clock.UtcNow;
        var expiresAt = now + SelfSignedLifetime;
        _ledger.PanelCertificate.Replace(new SimulatedPanelCertificate(expiresAt, true, now));

        return Task.FromResult(Result<InstalledCertificateDto>.Ok(new InstalledCertificateDto(expiresAt)));
    }

    /// <inheritdoc />
    public Task<Result> WritePanelChallengeAsync(
        string token,
        string keyAuthorization,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var cap = _ledger.Limits.MaxChallengeTextLength;
        var wellFormed = SimulatedInput.IsToken(token, cap)
            && keyAuthorization.Length > 0
            && SimulatedInput.IsPlainText(keyAuthorization, cap);
        if (!wellFormed)
        {
            return Task.FromResult(Result.Fail(SimulatedAgentErrors.InvalidInput()));
        }

        var result = _ledger.Atomically(
            () =>
            {
                var challenge = new SimulatedPanelChallenge(token, keyAuthorization, _clock.UtcNow);

                // Writing the same token again rewrites the file, as it does on a real host.
                if (_ledger.PanelChallenges.Replace(token, _ => { return challenge; }))
                {
                    return Result.Ok();
                }

                return _ledger.PanelChallenges.Add(token, challenge) == SimulatedLedgerAdd.Added
                    ? Result.Ok()
                    : Result.Fail(SimulatedAgentErrors.LimitReached());
            });

        return Task.FromResult(result);
    }

    /// <inheritdoc />
    public Task<Result> RemovePanelChallengeAsync(string token, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!SimulatedInput.IsToken(token, _ledger.Limits.MaxChallengeTextLength))
        {
            return Task.FromResult(Result.Fail(SimulatedAgentErrors.InvalidInput()));
        }

        return Task.FromResult(
            _ledger.PanelChallenges.Remove(token) ? Result.Ok() : Result.Fail(SimulatedAgentErrors.NotFound()));
    }

    /// <summary>Whether neither piece of key material is larger than the caps allow.</summary>
    /// <param name="certificatePem">The certificate text.</param>
    /// <param name="privateKeyPem">The private key text.</param>
    /// <returns>True when both are within their caps.</returns>
    private bool IsWithinSizeCaps(string certificatePem, string privateKeyPem)
    {
        return certificatePem.Length <= _ledger.Limits.MaxCertificatePemBytes
            && privateKeyPem.Length <= _ledger.Limits.MaxPrivateKeyPemBytes;
    }
}
