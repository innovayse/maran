using Maran.Modules.Ssl.Jobs;
using Maran.Modules.Ssl.Tests.TestSupport;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maran.Modules.Ssl.Tests.Jobs;

/// <summary>What the renewal pass does about the operator's expiry alert (issue #161).</summary>
public sealed class CertificateRenewalExpiryAlertTests
{
    /// <summary>An authority refusal that makes every order fail.</summary>
    private static readonly Error Refusal = Error.Of("AcmeValidationFailed", ErrorType.UpstreamRefused);

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

    /// <summary>Seeds one certificate for a fixture's site.</summary>
    /// <param name="fixture">The fixture to seed.</param>
    /// <param name="domain">The domain, which must be one the fixture owns.</param>
    /// <param name="daysToExpiry">How far in the future the certificate expires.</param>
    private static async Task SeedAsync(SslHandlerFixture fixture, string domain, int daysToExpiry)
    {
        fixture.DbContext.Certificates.Add(SslTestContext.Certificate(
            fixture.AccountId,
            domain,
            SslHandlerFixture.Now.AddDays(daysToExpiry),
            siteId: fixture.SiteFor(domain).Id));
        await fixture.DbContext.SaveChangesAsync();
    }

    /// <summary>A failing certificate inside the alert window alerts once and the mark is saved.</summary>
    [Fact]
    public async Task A_failing_certificate_inside_the_alert_window_alerts_once_and_the_mark_is_saved()
    {
        using var fixture = new SslHandlerFixture(["soon.example.com"], acmeFailure: Refusal);
        await SeedAsync(fixture, "soon.example.com", 5);

        await HandlerFor(fixture).HandleAsync(new CertificateRenewalRequested(), CancellationToken.None);

        Assert.Equal(["soon.example.com"], fixture.Alerts.Raised);
        fixture.DbContext.ChangeTracker.Clear();
        var stored = await fixture.DbContext.Certificates.SingleAsync();
        Assert.Equal(SslHandlerFixture.Now, stored.ExpiryAlertedAt);
    }

    /// <summary>A second pass over the same still-failing certificate does not alert again.</summary>
    [Fact]
    public async Task A_second_pass_over_the_same_failing_certificate_does_not_alert_again()
    {
        using var fixture = new SslHandlerFixture(["soon.example.com"], acmeFailure: Refusal);
        await SeedAsync(fixture, "soon.example.com", 5);

        await HandlerFor(fixture).HandleAsync(new CertificateRenewalRequested(), CancellationToken.None);
        await HandlerFor(fixture).HandleAsync(new CertificateRenewalRequested(), CancellationToken.None);

        Assert.Single(fixture.Alerts.Raised);
    }

    /// <summary>A certificate inside the renewal window but outside the alert window does not alert.</summary>
    [Fact]
    public async Task A_failing_certificate_outside_the_alert_window_does_not_alert()
    {
        using var fixture = new SslHandlerFixture(["early.example.com"], acmeFailure: Refusal);
        await SeedAsync(fixture, "early.example.com", 20);

        await HandlerFor(fixture).HandleAsync(new CertificateRenewalRequested(), CancellationToken.None);

        Assert.Empty(fixture.Alerts.Raised);
        var stored = await fixture.DbContext.Certificates.SingleAsync();
        Assert.Null(stored.ExpiryAlertedAt);
    }

    /// <summary>A certificate the failure backoff skips still alerts when it is inside the window.</summary>
    [Fact]
    public async Task A_certificate_skipped_by_the_failure_backoff_still_alerts_inside_the_window()
    {
        using var fixture = new SslHandlerFixture(["dead.example.com"]);
        await SeedAsync(fixture, "dead.example.com", 5);
        var certificate = await fixture.DbContext.Certificates.SingleAsync();
        certificate.RenewalFailed("AcmeValidationFailed", SslHandlerFixture.Now);
        certificate.RenewalFailed("AcmeValidationFailed", SslHandlerFixture.Now);
        certificate.RenewalFailed("AcmeValidationFailed", SslHandlerFixture.Now);
        await fixture.DbContext.SaveChangesAsync();

        await HandlerFor(fixture).HandleAsync(new CertificateRenewalRequested(), CancellationToken.None);

        Assert.Empty(fixture.Acme.Orders);
        Assert.Equal(["dead.example.com"], fixture.Alerts.Raised);
    }

    /// <summary>A certificate far from expiry is never offered to the alert at all.</summary>
    [Fact]
    public async Task A_certificate_outside_the_renewal_window_is_not_offered_to_the_alert()
    {
        using var fixture = new SslHandlerFixture(["later.example.com"]);
        await SeedAsync(fixture, "later.example.com", 60);

        await HandlerFor(fixture).HandleAsync(new CertificateRenewalRequested(), CancellationToken.None);

        Assert.Empty(fixture.Alerts.Offered);
    }

    /// <summary>A mail that cannot be raised neither fails the pass nor undoes the renewal failure record.</summary>
    [Fact]
    public async Task An_alert_failure_does_not_fail_the_pass_and_leaves_no_alert_mark()
    {
        using var fixture = new SslHandlerFixture(["soon.example.com", "next.example.com"], acmeFailure: Refusal);
        await SeedAsync(fixture, "soon.example.com", 4);
        await SeedAsync(fixture, "next.example.com", 6);
        fixture.Alerts.Throws = true;

        var renewed = await HandlerFor(fixture).HandleAsync(new CertificateRenewalRequested(), CancellationToken.None);

        Assert.Equal(0, renewed);
        Assert.Equal(["soon.example.com", "next.example.com"], fixture.Alerts.Offered);
        fixture.DbContext.ChangeTracker.Clear();
        var stored = await fixture.DbContext.Certificates.ToListAsync();
        Assert.All(stored, certificate =>
        {
            Assert.Equal(1, certificate.ConsecutiveRenewalFailures);
            Assert.Null(certificate.ExpiryAlertedAt);
        });
    }

    /// <summary>A successful renewal clears an earlier alert mark and does not alert again.</summary>
    [Fact]
    public async Task A_successful_renewal_clears_the_alert_mark_and_does_not_alert()
    {
        using var fixture = new SslHandlerFixture(["soon.example.com"]);
        await SeedAsync(fixture, "soon.example.com", 5);
        var certificate = await fixture.DbContext.Certificates.SingleAsync();
        certificate.MarkExpiryAlerted(SslHandlerFixture.Now.AddDays(-1));
        await fixture.DbContext.SaveChangesAsync();

        var renewed = await HandlerFor(fixture).HandleAsync(new CertificateRenewalRequested(), CancellationToken.None);

        Assert.Equal(1, renewed);
        Assert.Empty(fixture.Alerts.Offered);
        fixture.DbContext.ChangeTracker.Clear();
        var stored = await fixture.DbContext.Certificates.SingleAsync();
        Assert.Null(stored.ExpiryAlertedAt);
    }
}
