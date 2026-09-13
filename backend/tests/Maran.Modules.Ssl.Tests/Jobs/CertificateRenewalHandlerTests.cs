using Maran.Modules.Ssl.Domain.Enums;
using Maran.Modules.Ssl.Interfaces;
using Maran.Modules.Ssl.Jobs;
using Maran.Modules.Ssl.Services;
using Maran.Modules.Ssl.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maran.Modules.Ssl.Tests.Jobs;

/// <summary>The unattended renewal pass, driven through its own entry point on an injected clock.</summary>
public sealed class CertificateRenewalHandlerTests
{
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
            fixture.Clock,
            NullLogger<CertificateRenewalHandler>.Instance,
            fixture.Tasks);
    }

    /// <summary>Seeds one certificate for a fixture's site.</summary>
    /// <param name="fixture">The fixture to seed.</param>
    /// <param name="domain">The domain, which must be one the fixture owns.</param>
    /// <param name="daysToExpiry">How far in the future the certificate expires.</param>
    /// <param name="source">Where the certificate came from.</param>
    private static async Task SeedAsync(
        SslHandlerFixture fixture,
        string domain,
        int daysToExpiry,
        CertificateSource source = CertificateSource.Acme)
    {
        fixture.DbContext.Certificates.Add(SslTestContext.Certificate(
            fixture.AccountId,
            domain,
            SslHandlerFixture.Now.AddDays(daysToExpiry),
            source,
            fixture.SiteFor(domain).Id));
        await fixture.DbContext.SaveChangesAsync();
    }

    /// <summary>A certificate at twenty nine days is renewed and one at thirty one is left alone.</summary>
    [Fact]
    public async Task A_certificate_at_twenty_nine_days_is_renewed_and_one_at_thirty_one_is_left_alone()
    {
        using var fixture = new SslHandlerFixture(["soon.example.com", "later.example.com"]);
        await SeedAsync(fixture, "soon.example.com", 29);
        await SeedAsync(fixture, "later.example.com", 31);

        var renewed = await HandlerFor(fixture).HandleAsync(new CertificateRenewalRequested(), CancellationToken.None);

        Assert.Equal(1, renewed);
        var order = Assert.Single(fixture.Acme.Orders);
        Assert.Equal("soon.example.com", order.Domain);
    }

    /// <summary>A customer supplied certificate is never re ordered however close its expiry is.</summary>
    [Fact]
    public async Task A_customer_supplied_certificate_is_never_re_ordered_however_close_its_expiry_is()
    {
        using var fixture = new SslHandlerFixture(["custom.example.com"]);
        await SeedAsync(fixture, "custom.example.com", 1, CertificateSource.Custom);

        var renewed = await HandlerFor(fixture).HandleAsync(new CertificateRenewalRequested(), CancellationToken.None);

        Assert.Equal(0, renewed);
        Assert.Empty(fixture.Acme.Orders);
    }

    /// <summary>A renewal moves the expiry and clears the failure state.</summary>
    [Fact]
    public async Task A_renewal_moves_the_expiry_and_clears_the_failure_state()
    {
        using var fixture = new SslHandlerFixture(["soon.example.com"]);
        await SeedAsync(fixture, "soon.example.com", 5);

        await HandlerFor(fixture).HandleAsync(new CertificateRenewalRequested(), CancellationToken.None);

        var stored = await fixture.DbContext.Certificates.SingleAsync();
        Assert.Equal(new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero), stored.NotAfter);
        Assert.Equal(0, stored.ConsecutiveRenewalFailures);
        Assert.Equal(SslHandlerFixture.Now, stored.LastRenewalAttemptAt);
    }

    /// <summary>The web server is reloaded exactly once for a whole pass.</summary>
    [Fact]
    public async Task The_web_server_is_reloaded_exactly_once_for_a_whole_pass()
    {
        using var fixture = new SslHandlerFixture(["a.example.com", "b.example.com", "c.example.com"]);
        await SeedAsync(fixture, "a.example.com", 1);
        await SeedAsync(fixture, "b.example.com", 2);
        await SeedAsync(fixture, "c.example.com", 3);

        var renewed = await HandlerFor(fixture).HandleAsync(new CertificateRenewalRequested(), CancellationToken.None);

        Assert.Equal(3, renewed);
        Assert.Equal(1, fixture.AgentSites.ReloadCallCount);
    }

    /// <summary>A pass that renewed nothing does not reload the web server.</summary>
    [Fact]
    public async Task A_pass_that_renewed_nothing_does_not_reload_the_web_server()
    {
        using var fixture = new SslHandlerFixture(["later.example.com"]);
        await SeedAsync(fixture, "later.example.com", 60);

        await HandlerFor(fixture).HandleAsync(new CertificateRenewalRequested(), CancellationToken.None);

        Assert.Equal(0, fixture.AgentSites.ReloadCallCount);
    }

    /// <summary>A failed renewal records a code on the row without moving the expiry.</summary>
    [Fact]
    public async Task A_failed_renewal_records_a_code_on_the_row_without_moving_the_expiry()
    {
        using var fixture = new SslHandlerFixture(
            ["soon.example.com"], acmeFailure: Error.Of("AcmeValidationFailed", ErrorType.Failure));
        await SeedAsync(fixture, "soon.example.com", 5);

        var renewed = await HandlerFor(fixture).HandleAsync(new CertificateRenewalRequested(), CancellationToken.None);

        Assert.Equal(0, renewed);
        var stored = await fixture.DbContext.Certificates.SingleAsync();
        Assert.Equal("AcmeValidationFailed", stored.LastRenewalErrorCode);
        Assert.Equal(1, stored.ConsecutiveRenewalFailures);
        Assert.Equal(SslHandlerFixture.Now.AddDays(5), stored.NotAfter);
    }

    /// <summary>A failed renewal is journalled so an operator can see it without watching the job.</summary>
    [Fact]
    public async Task A_failed_renewal_is_journalled_so_an_operator_can_see_it_without_watching_the_job()
    {
        using var fixture = new SslHandlerFixture(
            ["soon.example.com"], acmeFailure: Error.Of("AcmeValidationFailed", ErrorType.Failure));
        await SeedAsync(fixture, "soon.example.com", 5);

        await HandlerFor(fixture).HandleAsync(new CertificateRenewalRequested(), CancellationToken.None);

        var entry = Assert.Single(fixture.Audit.Entries);
        Assert.False(entry.Succeeded);
        Assert.Equal("soon.example.com", entry.Subject);
    }

    /// <summary>A renewal installs the material and marks the site as carrying a certificate.</summary>
    [Fact]
    public async Task A_renewal_installs_the_material_and_marks_the_site_as_carrying_a_certificate()
    {
        using var fixture = new SslHandlerFixture(["soon.example.com"]);
        await SeedAsync(fixture, "soon.example.com", 5);

        await HandlerFor(fixture).HandleAsync(new CertificateRenewalRequested(), CancellationToken.None);

        var install = Assert.Single(fixture.Agent.Installs);
        Assert.Equal(fixture.Username, install.Account);
        Assert.True(install.Site.HasCertificate);
        Assert.Equal([fixture.SiteFor("soon.example.com").Id], fixture.Sites.Attached);
    }

    /// <summary>A reload the agent refuses does not undo the renewals the pass already made.</summary>
    [Fact]
    public async Task A_reload_the_agent_refuses_does_not_undo_the_renewals_the_pass_already_made()
    {
        using var fixture = new SslHandlerFixture(
            ["soon.example.com"], reloadFailure: Error.Of("AgentValidationFailed", ErrorType.Validation));
        await SeedAsync(fixture, "soon.example.com", 5);

        var renewed = await HandlerFor(fixture).HandleAsync(new CertificateRenewalRequested(), CancellationToken.None);

        // The material is on disk and the row records it; the reload is a separate, retryable step.
        Assert.Equal(1, renewed);
        Assert.Equal(1, fixture.AgentSites.ReloadCallCount);
    }

    /// <summary>A certificate inside its failure backoff is skipped even though it is inside the window.</summary>
    [Fact]
    public async Task A_certificate_inside_its_failure_backoff_is_skipped_even_though_it_is_inside_the_window()
    {
        // The whole server shares one ACME registration, so a dead domain re-ordered every pass
        // spends a budget every other customer draws on.
        using var fixture = new SslHandlerFixture(["dead.example.com"]);
        await SeedAsync(fixture, "dead.example.com", 5);
        var certificate = await fixture.DbContext.Certificates.SingleAsync();
        certificate.RenewalFailed("AcmeValidationFailed", SslHandlerFixture.Now);
        certificate.RenewalFailed("AcmeValidationFailed", SslHandlerFixture.Now);
        certificate.RenewalFailed("AcmeValidationFailed", SslHandlerFixture.Now);
        await fixture.DbContext.SaveChangesAsync();

        var renewed = await HandlerFor(fixture).HandleAsync(new CertificateRenewalRequested(), CancellationToken.None);

        Assert.Equal(0, renewed);
        Assert.Empty(fixture.Acme.Orders);
    }

    /// <summary>A certificate whose backoff has elapsed is attempted again.</summary>
    [Fact]
    public async Task A_certificate_whose_backoff_has_elapsed_is_attempted_again()
    {
        // Guards the test above from passing for the wrong reason: backoff must delay attempts, not
        // end them, or a domain repaired after a few failures would never renew.
        using var fixture = new SslHandlerFixture(["repaired.example.com"]);
        await SeedAsync(fixture, "repaired.example.com", 5);
        var certificate = await fixture.DbContext.Certificates.SingleAsync();
        certificate.RenewalFailed("AcmeValidationFailed", SslHandlerFixture.Now.AddDays(-1));
        await fixture.DbContext.SaveChangesAsync();

        var renewed = await HandlerFor(fixture).HandleAsync(new CertificateRenewalRequested(), CancellationToken.None);

        Assert.Equal(1, renewed);
    }

    /// <summary>A certificate that throws does not discard the work of the ones before it.</summary>
    [Fact]
    public async Task A_certificate_that_throws_does_not_discard_the_work_of_the_ones_before_it()
    {
        using var fixture = new SslHandlerFixture(["first.example.com", "boom.example.com", "last.example.com"]);
        await SeedAsync(fixture, "first.example.com", 1);
        await SeedAsync(fixture, "boom.example.com", 2);
        await SeedAsync(fixture, "last.example.com", 3);
        var job = HandlerWithAcme(fixture, new ThrowingAcmeClient("boom.example.com", fixture.Acme.Material));

        var renewed = await job.HandleAsync(new CertificateRenewalRequested(), CancellationToken.None);

        Assert.Equal(2, renewed);

        fixture.DbContext.ChangeTracker.Clear();
        var stored = await fixture.DbContext.Certificates.ToListAsync();
        var newExpiry = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        Assert.Equal(newExpiry, Stored(stored, "first.example.com").NotAfter);
        Assert.Equal(newExpiry, Stored(stored, "last.example.com").NotAfter);

        // And the one that threw is recorded as failed rather than silently skipped.
        Assert.Equal(1, Stored(stored, "boom.example.com").ConsecutiveRenewalFailures);
    }

    /// <summary>Every renewal is saved before the next one starts.</summary>
    [Fact]
    public async Task Every_renewal_is_saved_before_the_next_one_starts()
    {
        using var fixture = new SslHandlerFixture(["a.example.com", "b.example.com"]);
        await SeedAsync(fixture, "a.example.com", 1);
        await SeedAsync(fixture, "b.example.com", 2);

        await HandlerFor(fixture).HandleAsync(new CertificateRenewalRequested(), CancellationToken.None);

        fixture.DbContext.ChangeTracker.Clear();
        var stored = await fixture.DbContext.Certificates.ToListAsync();
        Assert.All(stored, certificate =>
        {
            Assert.Equal(new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero), certificate.NotAfter);
        });
    }

    /// <summary>A renewal is journalled under a named system actor and not a blank one.</summary>
    [Fact]
    public async Task A_renewal_is_journalled_under_a_named_system_actor_and_not_a_blank_one()
    {
        // Outside a request ICurrentUser reports Guid.Empty and an empty name, which is what a failed
        // ANONYMOUS request also records — an operator could not tell the two apart.
        using var fixture = new SslHandlerFixture(["soon.example.com"]);
        await SeedAsync(fixture, "soon.example.com", 5);

        await HandlerFor(fixture).HandleAsync(new CertificateRenewalRequested(), CancellationToken.None);

        var entry = Assert.Single(fixture.Audit.Entries);
        Assert.Equal(SystemAuditEntry.NameFor(CertificateAuditJournal.ModuleName), entry.ActorUsername);
        Assert.Null(entry.ActorUserId);
        Assert.Equal(string.Empty, entry.IpAddress);
        Assert.Equal(string.Empty, entry.UserAgent);
    }

    /// <summary>Finds one stored certificate by domain.</summary>
    /// <param name="stored">Everything the pass left in the table.</param>
    /// <param name="domain">The domain to find.</param>
    /// <returns>That domain's row.</returns>
    private static Maran.Modules.Ssl.Domain.Entities.Certificate Stored(
        List<Maran.Modules.Ssl.Domain.Entities.Certificate> stored,
        string domain)
    {
        return stored.Single(certificate =>
        {
            return string.Equals(certificate.Domain, domain, StringComparison.Ordinal);
        });
    }

    /// <summary>Builds the job over a fixture but with a different acme client.</summary>
    /// <param name="fixture">The fixture supplying everything else.</param>
    /// <param name="acme">The certificate authority double to use.</param>
    /// <returns>The job under test.</returns>
    private static CertificateRenewalHandler HandlerWithAcme(SslHandlerFixture fixture, IAcmeClient acme)
    {
        return new CertificateRenewalHandler(
            fixture.DbContext,
            fixture.Sites,
            fixture.Accounts,
            acme,
            fixture.Installer,
            fixture.AgentSites,
            fixture.Journal,
            fixture.Clock,
            NullLogger<CertificateRenewalHandler>.Instance,
            fixture.Tasks);
    }
}
