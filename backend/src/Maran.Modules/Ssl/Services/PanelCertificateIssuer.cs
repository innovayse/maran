using Maran.Agent.Client.Interfaces;
using Maran.Modules.Ssl.Domain.Entities;
using Maran.Modules.Ssl.Interfaces;
using Maran.Modules.Ssl.Models;
using Maran.Modules.Ssl.Persistence;

namespace Maran.Modules.Ssl.Services;

/// <summary>
/// Orders a certificate for the panel's own host name, has the agent install it as the panel's pair,
/// and records it — the one path both the administrator's request and the daily renewal take
/// (issue #132).
/// </summary>
/// <remarks>
/// The order of the steps is the fallback. The authority is asked FIRST, through the same
/// <see cref="IAcmeClient"/> every site certificate is ordered with; its HTTP-01 file goes to the
/// panel's own challenge directory (<see cref="PanelChallengeDirectory"/>), refused below the agent's
/// free-space floor like any other write. Only material the authority issued is sent to the agent,
/// which replaces the pair with nginx's validation and a rollback. The row is written LAST, so it
/// never describes material that is not on disk. A failure at any step leaves the pair the panel was
/// serving — the installer's self-signed one, or the previous trusted one — untouched.
/// </remarks>
public sealed class PanelCertificateIssuer
{
    /// <summary>The Ssl module's database context.</summary>
    private readonly SslDbContext _dbContext;

    /// <summary>The certificate authority.</summary>
    private readonly IAcmeClient _acme;

    /// <summary>The agent's TLS operations, which own the panel's pair.</summary>
    private readonly IAgentSslClient _agent;

    /// <summary>The injected time source; never the ambient clock (rules/csharp.md).</summary>
    private readonly IClock _clock;

    /// <summary>Creates the issuer.</summary>
    /// <param name="dbContext">The Ssl module's database context.</param>
    /// <param name="acme">The certificate authority.</param>
    /// <param name="agent">The agent's TLS operations.</param>
    /// <param name="clock">The injected time source.</param>
    public PanelCertificateIssuer(SslDbContext dbContext, IAcmeClient acme, IAgentSslClient agent, IClock clock)
    {
        _dbContext = dbContext;
        _acme = acme;
        _agent = agent;
        _clock = clock;
    }

    /// <summary>Orders, installs and records a certificate for <paramref name="hostName"/>.</summary>
    /// <param name="hostName">The panel's configured host name.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The recorded certificate, or the authority's or the agent's own typed failure.</returns>
    public async Task<Result<PanelCertificate>> IssueAsync(string hostName, CancellationToken cancellationToken)
    {
        var issued = await _acme.OrderAsync(
            new AcmeOrderRequest(hostName, new PanelChallengeDirectory()), cancellationToken);
        if (!issued.IsSuccess)
        {
            return Result<PanelCertificate>.Fail(issued.Error!);
        }

        var installed = await _agent.InstallPanelCertificateAsync(
            issued.Value.CertificatePem, issued.Value.PrivateKeyPem, cancellationToken);
        if (!installed.IsSuccess)
        {
            return Result<PanelCertificate>.Fail(installed.Error!);
        }

        var certificate = await _dbContext.PanelCertificates
            .SingleOrDefaultAsync(row => row.HostName == hostName, cancellationToken);
        if (certificate is null)
        {
            certificate = new PanelCertificate(hostName, installed.Value.ExpiresAt, _clock.UtcNow);
            _dbContext.PanelCertificates.Add(certificate);
        }
        else
        {
            certificate.Reissued(installed.Value.ExpiresAt, _clock.UtcNow);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result<PanelCertificate>.Ok(certificate);
    }
}
