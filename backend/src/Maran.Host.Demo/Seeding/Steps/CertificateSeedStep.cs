using Maran.Host.Demo.Seeding.Interfaces;
using Maran.Host.Demo.Seeding.Models;
using Maran.Host.Demo.Seeding.Services;
using Maran.Host.Demo.World.Enums;
using Maran.Modules.Ssl.Commands.IssueCertificate;
using Maran.Modules.Ssl.Common;
using Maran.Modules.Ssl.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Maran.Host.Demo.Seeding.Steps;

/// <summary>
/// Issues a certificate for every site that carries one, then moves the expiring and the failing ones
/// into their state through the certificate's own domain methods.
/// </summary>
/// <remarks>
/// No command produces a certificate near expiry or one whose renewal failed, so those two states are
/// written by calling <c>Renewed</c> and <c>RenewalFailed</c> on the stored row. The failing certificate
/// records enough consecutive failures that the renewal job's back-off holds it back for days, which is
/// longer than a reset interval. The expiring one has no such hold: the renewal job may renew it within
/// its first sweep, which the next reset undoes.
/// </remarks>
public sealed class CertificateSeedStep : IDemoSeedStep
{
    /// <summary>Days left on the expiring certificate; inside the expiry-alert window.</summary>
    private const int ExpiringDaysLeft = 10;

    /// <summary>Days left on the certificate whose renewal fails.</summary>
    private const int FailingDaysLeft = 6;

    /// <summary>Consecutive failures recorded, enough for a back-off of several days.</summary>
    private const int ConsecutiveFailures = 8;

    /// <summary>The failure code the renewal records.</summary>
    private const string FailureCode = "AcmeAuthorityUnreachable";

    /// <summary>Runs commands through the real handlers.</summary>
    private readonly DemoSeedCommandRunner _runner;

    /// <summary>Opens the scope the certificate context is resolved in.</summary>
    private readonly IServiceScopeFactory _scopeFactory;

    /// <summary>Reads the instant.</summary>
    private readonly IClock _clock;

    /// <summary>Creates the step.</summary>
    /// <param name="runner">Runs commands through the real handlers.</param>
    /// <param name="scopeFactory">Opens the scope the certificate context is resolved in.</param>
    /// <param name="clock">Reads the instant.</param>
    public CertificateSeedStep(DemoSeedCommandRunner runner, IServiceScopeFactory scopeFactory, IClock clock)
    {
        _runner = runner;
        _scopeFactory = scopeFactory;
        _clock = clock;
    }

    /// <inheritdoc />
    public string Name
    {
        get { return "certificates"; }
    }

    /// <inheritdoc />
    public async Task RunAsync(DemoSeedContext context, CancellationToken cancellationToken)
    {
        var certified = context.World.Sites
            .Where(site => { return site.Enabled && site.Certificate != DemoCertificateState.None; })
            .ToList();

        foreach (var site in certified)
        {
            await _runner.RunAsync<CertificateDto>(
                Name,
                new IssueCertificateCommand(site.Domain, DemoSeedCommandRunner.Origin, DemoSeedCommandRunner.Agent),
                cancellationToken);
        }

        await using var scope = _scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<SslDbContext>();
        var now = _clock.UtcNow;

        foreach (var site in certified.Where(site =>
        {
            return site.Certificate is DemoCertificateState.ExpiringSoon or DemoCertificateState.RenewalFailed;
        }))
        {
            var certificate = await dbContext.Certificates
                .SingleAsync(row => row.Domain == site.Domain, cancellationToken);

            if (site.Certificate == DemoCertificateState.ExpiringSoon)
            {
                certificate.Renewed(now.AddDays(ExpiringDaysLeft), now);
                continue;
            }

            certificate.Renewed(now.AddDays(FailingDaysLeft), now);
            for (var attempt = 0; attempt < ConsecutiveFailures; attempt++)
            {
                certificate.RenewalFailed(FailureCode, now);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
