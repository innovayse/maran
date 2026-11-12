using Maran.Modules.Ssl.Jobs;
using Maran.Modules.Ssl.Services;
using Maran.Modules.Ssl.Tests.TestSupport;
using Maran.Sdk.Modules;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maran.Modules.Ssl.Tests.Jobs;

/// <summary>
/// The renewal pass takes the site's operation lock to install, so a renewal never rewrites a vhost
/// while another operation is rewriting the same site, and a site that stays busy is recorded as a
/// failed attempt rather than lost.
/// </summary>
public sealed class CertificateRenewalSiteLockTests
{
    /// <summary>The domain every test here renews.</summary>
    private const string Domain = "soon.example.com";

    /// <summary>The longest any waiting step may take before the test calls it a hang.</summary>
    private static readonly TimeSpan Hang = TimeSpan.FromSeconds(10);

    /// <summary>Builds the job over a fixture's doubles.</summary>
    /// <param name="fixture">The fixture supplying the context and doubles.</param>
    /// <returns>The job under test.</returns>
    private static CertificateRenewalHandler HandlerFor(SslHandlerFixture fixture)
    {
        return new CertificateRenewalHandler(
            fixture.DbContext,
            fixture.Sites,
            fixture.Accounts,
            fixture.Acme,
            fixture.Installer,
            fixture.AgentSites,
            fixture.Journal,
            fixture.Alerts,
            fixture.Clock,
            NullLogger<CertificateRenewalHandler>.Instance,
            fixture.Tasks,
            fixture.Stack);
    }

    /// <summary>Seeds one certificate inside the renewal window for the fixture's site.</summary>
    /// <param name="fixture">The fixture to seed.</param>
    private static async Task SeedAsync(SslHandlerFixture fixture)
    {
        fixture.DbContext.Certificates.Add(SslTestContext.Certificate(
            fixture.AccountId,
            Domain,
            SslHandlerFixture.Now.AddDays(5),
            siteId: fixture.SiteFor(Domain).Id));
        await fixture.DbContext.SaveChangesAsync();
    }

    /// <summary>A site that stays busy is recorded as a failed attempt, with the install never reaching the host.</summary>
    [Fact]
    public async Task A_site_that_stays_busy_is_recorded_as_a_failed_attempt_and_the_host_is_left_alone()
    {
        var busy = new BusySiteLock();
        using var fixture = new SslHandlerFixture([Domain], siteLock: busy);
        await SeedAsync(fixture);

        var renewed = await HandlerFor(fixture).HandleAsync(new CertificateRenewalRequested(), CancellationToken.None);

        Assert.Equal(0, renewed);
        var stored = await fixture.DbContext.Certificates.SingleAsync();
        Assert.Equal("SiteBusy", stored.LastRenewalFailureCode);
        Assert.Equal(1, stored.ConsecutiveRenewalFailures);
        Assert.Empty(fixture.Agent.Installs);
        Assert.Empty(fixture.Sites.Attached);
        Assert.Equal(0, fixture.AgentSites.ReloadCallCount);
        Assert.Equal("SiteBusy", Assert.Single(fixture.Tasks.Tasks).FailureCode);
    }

    /// <summary>Unattended work asks for the long wait, not the interactive one.</summary>
    [Fact]
    public async Task Unattended_work_asks_for_the_long_wait()
    {
        var busy = new BusySiteLock();
        using var fixture = new SslHandlerFixture([Domain], siteLock: busy);
        await SeedAsync(fixture);

        await HandlerFor(fixture).HandleAsync(new CertificateRenewalRequested(), CancellationToken.None);

        var attempt = Assert.Single(busy.Attempts);
        Assert.Equal(fixture.SiteFor(Domain).Id, attempt.SiteId);
        Assert.Equal(CertificateLockWait.Unattended, attempt.Wait);
        Assert.True(CertificateLockWait.Unattended > CertificateLockWait.Interactive);
    }

    /// <summary>The accepting control: with the site free, the same pass renews.</summary>
    [Fact]
    public async Task With_the_site_free_the_same_pass_renews()
    {
        using var fixture = new SslHandlerFixture([Domain]);
        await SeedAsync(fixture);

        var renewed = await HandlerFor(fixture).HandleAsync(new CertificateRenewalRequested(), CancellationToken.None);

        Assert.Equal(1, renewed);
        Assert.Equal(0, (await fixture.DbContext.Certificates.SingleAsync()).ConsecutiveRenewalFailures);
    }

    /// <summary>
    /// The barrier: a renewal that arrives while another operation holds the site installs nothing until
    /// the holder lets go, then renews without recording a failure.
    /// </summary>
    [Fact]
    public async Task A_renewal_waits_for_the_holder_and_installs_only_after_it_lets_go()
    {
        var locks = new ParkingSiteLock(new SiteOperationLock());
        using var fixture = new SslHandlerFixture([Domain], siteLock: locks);
        await SeedAsync(fixture);
        var held = (await fixture.SiteLocks.AcquireAsync(
            fixture.SiteFor(Domain).Id, TimeSpan.Zero, CancellationToken.None)).Value;

        var pass = HandlerFor(fixture).HandleAsync(new CertificateRenewalRequested(), CancellationToken.None);

        // Parked inside the lock is an event, not a pause: a lock that let the renewal through never parks
        // it, and this wait then times out instead of the test passing on a slow machine.
        await locks.Parked.WaitAsync(Hang);

        Assert.False(pass.IsCompleted);
        Assert.Empty(fixture.Agent.Installs);

        await held.DisposeAsync();
        var renewed = await pass.WaitAsync(Hang);

        Assert.Equal(1, renewed);
        Assert.Single(fixture.Agent.Installs);
        Assert.Equal(0, (await fixture.DbContext.Certificates.SingleAsync()).ConsecutiveRenewalFailures);
    }
}
