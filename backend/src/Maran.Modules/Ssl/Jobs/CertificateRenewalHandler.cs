using Maran.Agent.Client.Interfaces;
using Maran.Modules.Ssl.Common;
using Maran.Modules.Ssl.Common.Interfaces;
using Maran.Modules.Ssl.Domain;
using Maran.Modules.Ssl.Persistence;
using Maran.Modules.Ssl.Resources;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Microsoft.Extensions.Logging;

namespace Maran.Modules.Ssl.Jobs;

/// <summary>
/// Re-orders every ACME certificate that expires within thirty days, installs the new material, and
/// reloads the web server once at the end (spec §11).
/// </summary>
/// <remarks>
/// A scheduled message handler, not a daemon: rules/security.md item 10 forbids a new process
/// without a spec change, and the panel already has a durable scheduler in its message bus. The Host
/// schedules <see cref="CertificateRenewalRequested"/>; this type is what runs when it arrives.
///
/// The NAME is load-bearing and is why this type is not called <c>…Job</c>. Wolverine discovers
/// handlers by convention — a type whose name ends in <c>Handler</c> (or <c>Consumer</c>) with a
/// public <c>HandleAsync</c> — and it discovers nothing else. Named <c>CertificateRenewalJob</c>
/// this type was fully implemented, fully unit-tested and never once executed: the daily publish
/// logged <c>No routes can be determined for Envelope … CertificateRenewalRequested</c> and every
/// certificate the panel issued would have expired unwatched. Every unit test stayed green because
/// they all call <see cref="HandleAsync"/> directly, which is precisely the gap a test resolving
/// the handler from the real container closes
/// (<c>CertificateRenewalSchedulingTests</c>, rules/testing.md).
///
/// Thirty days is the industry's standard head start on a ninety-day certificate: it leaves two full
/// months of daily retries before anything actually expires, so a domain whose DNS is being moved, or
/// an authority having a bad week, costs an operator a warning rather than an outage.
///
/// The clock is INJECTED, which is what makes the window testable. A test can stand a certificate at
/// twenty-nine days and one at thirty-one and assert which is selected, without waiting sixty days
/// and without the test itself computing the boundary — the ambient clock is a banned API here for
/// exactly this reason (rules/csharp.md).
///
/// It runs unfiltered over every account on the server, which is stated out loud in the query below.
/// It has no authenticated caller: it is not acting FOR a customer, it is acting for the operator,
/// and a fabricated administrator principal would be a principal that other code could then be
/// resolved with.
///
/// One reload at the end, not one per site: a renewal pass can touch every site on a busy server, and
/// reloading per certificate would mean dozens of reloads where one would do. That is exactly what
/// <c>ReloadWebServerAsync</c> is for.
/// </remarks>
public sealed class CertificateRenewalHandler
{
    /// <summary>How far ahead of expiry a certificate is re-ordered.</summary>
    public static readonly TimeSpan RenewalWindow = TimeSpan.FromDays(30);

    /// <summary>Pre-compiled log delegate for a renewal that did not produce new material.</summary>
    private static readonly Action<ILogger, string, string, int, Exception?> LogRenewalFailure =
        LoggerMessage.Define<string, string, int>(
            LogLevel.Warning,
            new EventId(1, nameof(CertificateRenewalHandler)),
            "Renewal of {Domain} failed with {ErrorCode}; {Failures} consecutive failures so far");

    /// <summary>Pre-compiled log delegate for a renewal that threw rather than returning a failure.</summary>
    private static readonly Action<ILogger, string, Exception?> LogRenewalThrew =
        LoggerMessage.Define<string>(
            LogLevel.Error,
            new EventId(3, nameof(CertificateRenewalHandler)),
            "Renewal of {Domain} threw; the pass continues with the next certificate");

    /// <summary>Pre-compiled log delegate for a reload the agent refused after a renewal pass.</summary>
    private static readonly Action<ILogger, string, Exception?> LogReloadFailure =
        LoggerMessage.Define<string>(
            LogLevel.Error,
            new EventId(2, nameof(CertificateRenewalHandler)),
            "Renewal pass installed new certificates but the web server reload failed with {ErrorCode}");

    /// <summary>The Ssl module's database context.</summary>
    private readonly SslDbContext _dbContext;

    /// <summary>The unscoped window onto site rows, which renewal needs because it serves no tenant.</summary>
    private readonly ISiteDirectory _sites;

    /// <summary>The owning account's system user name, which every agent operation is addressed by.</summary>
    private readonly IAccountDirectory _accounts;

    /// <summary>The certificate authority.</summary>
    private readonly IAcmeClient _acme;

    /// <summary>The one path that puts material on the host and marks the site as carrying it.</summary>
    private readonly CertificateInstaller _installer;

    /// <summary>The agent's batch reload, called once when the pass has installed anything.</summary>
    private readonly IAgentSitesClient _agentSites;

    /// <summary>This module's audit journal.</summary>
    private readonly CertificateAuditJournal _journal;

    /// <summary>The injected time source, which defines the renewal window.</summary>
    private readonly IClock _clock;

    /// <summary>Where a renewal failure becomes visible to an operator, since nobody is watching the call.</summary>
    private readonly ILogger<CertificateRenewalHandler> _logger;

    /// <summary>Creates the job.</summary>
    /// <param name="dbContext">The Ssl module's database context.</param>
    /// <param name="sites">The window onto site rows.</param>
    /// <param name="accounts">The owning account's system user name.</param>
    /// <param name="acme">The certificate authority.</param>
    /// <param name="installer">The shared install path.</param>
    /// <param name="agentSites">The agent's batch web-server reload.</param>
    /// <param name="journal">This module's audit journal.</param>
    /// <param name="clock">The injected time source defining the renewal window.</param>
    /// <param name="logger">Sink for renewal failures.</param>
    public CertificateRenewalHandler(
        SslDbContext dbContext,
        ISiteDirectory sites,
        IAccountDirectory accounts,
        IAcmeClient acme,
        CertificateInstaller installer,
        IAgentSitesClient agentSites,
        CertificateAuditJournal journal,
        IClock clock,
        ILogger<CertificateRenewalHandler> logger)
    {
        _dbContext = dbContext;
        _sites = sites;
        _accounts = accounts;
        _acme = acme;
        _installer = installer;
        _agentSites = agentSites;
        _journal = journal;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>Runs one renewal pass over every certificate due within the window.</summary>
    /// <param name="message">The scheduled trigger; it carries no parameters.</param>
    /// <param name="cancellationToken">Cancels the pass.</param>
    /// <returns>How many certificates were successfully renewed.</returns>
    /// <remarks>
    /// Each certificate is its own unit of work: renewed, recorded and SAVED before the next one is
    /// started, and anything it throws is caught and recorded rather than allowed to end the pass.
    ///
    /// Both halves of that matter and an earlier version had neither. With one save after the loop, a
    /// throw on the seventh certificate discarded the row updates for six that had already been
    /// ordered, installed and reloaded — so the next pass re-ordered material that was already on
    /// disk, at the authority's expense, and every recorded failure code went with it. And an
    /// uncaught throw meant one unreachable domain ended the pass for every certificate behind it in
    /// the queue, which is one site expiring turning into all of them.
    /// </remarks>
    public async Task<int> HandleAsync(CertificateRenewalRequested message, CancellationToken cancellationToken)
    {
        var due = await SelectDueAsync(cancellationToken);
        var renewed = 0;

        foreach (var certificate in due)
        {
            if (await RenewSafelyAsync(certificate, cancellationToken))
            {
                renewed += 1;
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        if (renewed > 0)
        {
            var reloaded = await _agentSites.ReloadWebServerAsync(cancellationToken);
            if (!reloaded.IsSuccess)
            {
                // Loud, and Error rather than Warning: new certificates are on disk and the running
                // web server is still serving the old ones. Nothing is broken yet and everything is
                // about to be.
                LogReloadFailure(_logger, reloaded.Error!.Code, null);
            }
        }

        return renewed;
    }

    /// <summary>Renews one certificate, turning anything it throws into a recorded failure.</summary>
    /// <param name="certificate">The certificate to renew.</param>
    /// <param name="cancellationToken">Cancels the renewal.</param>
    /// <returns><c>true</c> when new material was installed.</returns>
    /// <remarks>
    /// The catch is deliberately broad and deliberately does not include cancellation. A shutdown or
    /// a caller's cancellation must stop the pass — swallowing it would keep the process ordering
    /// certificates while it was being asked to exit — whereas an unreachable authority, a refused
    /// socket or a failed journal write is one certificate's problem and must not become every
    /// certificate's.
    /// </remarks>
    private async Task<bool> RenewSafelyAsync(Certificate certificate, CancellationToken cancellationToken)
    {
        try
        {
            return await RenewAsync(certificate, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogRenewalThrew(_logger, certificate.Domain, exception);
            certificate.RenewalFailed(nameof(ErrorMessages.AcmeAuthorityUnreachable), _clock.UtcNow);
            return false;
        }
    }

    /// <summary>Reads every certificate the panel may and should re-order now.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The due certificates, soonest expiry first.</returns>
    /// <remarks>
    /// <c>IgnoreQueryFilters</c> is deliberate and is the only place in this module that says it for
    /// a tenant-scoped entity: renewal runs for the whole server and has no authenticated caller, so
    /// there is no tenant to be scoped to.
    ///
    /// The SQL clause is a PREFILTER and nothing more — it narrows the table to the rows that could
    /// possibly be due, using the indexed <c>NotAfter</c> column, so a server with ten thousand
    /// certificates does not materialise all of them. The DECISION is
    /// <see cref="Certificate.IsDueForRenewal"/>, applied to what comes back. Restating the rule in
    /// LINQ would be a second definition that nothing keeps in step with the first, which is exactly
    /// what this used to be: the failure backoff existed on neither side, and mutating one predicate
    /// left the other's tests green.
    /// </remarks>
    private async Task<List<Certificate>> SelectDueAsync(CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var deadline = now + RenewalWindow;

        var candidates = await _dbContext.Certificates
            .IgnoreQueryFilters()
            .Where(certificate => certificate.NotAfter <= deadline)
            .OrderBy(certificate => certificate.NotAfter)
            .ToListAsync(cancellationToken);

        return candidates
            .Where(certificate =>
            {
                return certificate.IsDueForRenewal(now, RenewalWindow);
            })
            .ToList();
    }

    /// <summary>Re-orders and reinstalls one certificate.</summary>
    /// <param name="certificate">The certificate to renew. Its row is updated either way.</param>
    /// <param name="cancellationToken">Cancels the renewal.</param>
    /// <returns><c>true</c> when new material was installed.</returns>
    /// <remarks>
    /// Every failure path here records a code on the row and journals the attempt. It does NOT claim
    /// to be exception-free — it reaches an <c>HttpClient</c>, a <c>Task.Delay</c> and the database,
    /// any of which can throw — which is why <see cref="RenewSafelyAsync"/> wraps it. A previous
    /// version of this remark asserted "none of them throws", and that assertion was simply false.
    /// </remarks>
    private async Task<bool> RenewAsync(Certificate certificate, CancellationToken cancellationToken)
    {
        var site = await _sites.FindByIdUnscopedAsync(certificate.SiteId, cancellationToken);
        if (site is null)
        {
            return await RecordFailureAsync(certificate, nameof(ErrorMessages.SiteNotFound), cancellationToken);
        }

        var account = await _accounts.FindAsync(certificate.AccountId, cancellationToken);
        if (account is null)
        {
            return await RecordFailureAsync(certificate, nameof(ErrorMessages.AccountNotFound), cancellationToken);
        }

        var issued = await _acme.OrderAsync(
            new AcmeOrderRequest(certificate.Domain, account.Username), cancellationToken);
        if (!issued.IsSuccess)
        {
            return await RecordFailureAsync(certificate, issued.Error!.Code, cancellationToken);
        }

        var installed = await _installer.InstallAsync(
            account.Username, site, issued.Value.CertificatePem, issued.Value.PrivateKeyPem, cancellationToken);
        if (!installed.IsSuccess)
        {
            return await RecordFailureAsync(certificate, installed.Error!.Code, cancellationToken);
        }

        certificate.Renewed(installed.Value, _clock.UtcNow);

        await _journal.RecordScheduledAsync(
            AuditActions.CertificateRenewed, certificate.Domain, succeeded: true, cancellationToken);

        return true;
    }

    /// <summary>Records a failed renewal on the row, in the journal and in the log.</summary>
    /// <param name="certificate">The certificate whose renewal failed.</param>
    /// <param name="code">The machine-stable code of the failure. Never a supplied sentence.</param>
    /// <param name="cancellationToken">Cancels the journal write.</param>
    /// <returns>Always <c>false</c>, so the caller can return it directly.</returns>
    private async Task<bool> RecordFailureAsync(
        Certificate certificate,
        string code,
        CancellationToken cancellationToken)
    {
        certificate.RenewalFailed(code, _clock.UtcNow);

        LogRenewalFailure(_logger, certificate.Domain, code, certificate.ConsecutiveRenewalFailures, null);

        await _journal.RecordScheduledAsync(
            AuditActions.CertificateRenewed, certificate.Domain, succeeded: false, cancellationToken);

        return false;
    }
}
