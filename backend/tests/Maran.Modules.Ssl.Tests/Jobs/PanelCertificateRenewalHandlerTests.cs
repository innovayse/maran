using Maran.Modules.Ssl.Domain.Entities;
using Maran.Modules.Ssl.Jobs;
using Maran.Modules.Ssl.Services;
using Maran.Modules.Ssl.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maran.Modules.Ssl.Tests.Jobs;

/// <summary>When the panel's own certificate is renewed, and what a failed renewal leaves.</summary>
public sealed class PanelCertificateRenewalHandlerTests
{
    /// <summary>The panel host name every test here is configured with.</summary>
    private const string PanelName = "panel.example.net";

    /// <summary>Builds the handler over the fixture.</summary>
    private static PanelCertificateRenewalHandler HandlerFor(SslHandlerFixture fixture, StubPanelHostName name)
    {
        return new PanelCertificateRenewalHandler(
            fixture.DbContext,
            name,
            new PanelCertificateIssuer(fixture.DbContext, fixture.Acme, fixture.Agent, fixture.Clock),
            fixture.Journal,
            fixture.Tasks,
            fixture.Clock,
            NullLogger<PanelCertificateRenewalHandler>.Instance);
    }

    /// <summary>Seeds a recorded certificate expiring <paramref name="days"/> after the fixture's now.</summary>
    private static async Task SeedAsync(SslHandlerFixture fixture, string hostName, int days)
    {
        fixture.DbContext.PanelCertificates.Add(
            new PanelCertificate(hostName, SslHandlerFixture.Now.AddDays(days), SslHandlerFixture.Now.AddDays(-60)));
        await fixture.DbContext.SaveChangesAsync();
        fixture.DbContext.ChangeTracker.Clear();
    }

    /// <summary>A certificate inside the window is re-ordered, installed and its expiry moved.</summary>
    [Fact]
    public async Task A_certificate_inside_the_window_is_reordered_installed_and_its_expiry_moved()
    {
        using var fixture = new SslHandlerFixture([]);
        await SeedAsync(fixture, PanelName, days: 10);

        var renewed = await HandlerFor(fixture, new StubPanelHostName(PanelName))
            .HandleAsync(new PanelCertificateRenewalRequested(), CancellationToken.None);

        Assert.True(renewed);
        Assert.Single(fixture.Agent.PanelInstalls);
        var stored = await fixture.DbContext.PanelCertificates.SingleAsync();
        Assert.Equal(new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero), stored.ExpiresAt);
        var entry = Assert.Single(fixture.Audit.Entries);
        Assert.Equal((AuditActions.CertificateRenewed, true), (entry.Action, entry.Succeeded));
    }

    /// <summary>A certificate outside the window is left alone.</summary>
    [Fact]
    public async Task A_certificate_outside_the_window_is_left_alone()
    {
        using var fixture = new SslHandlerFixture([]);
        await SeedAsync(fixture, PanelName, days: 31);

        var renewed = await HandlerFor(fixture, new StubPanelHostName(PanelName))
            .HandleAsync(new PanelCertificateRenewalRequested(), CancellationToken.None);

        Assert.False(renewed);
        Assert.Empty(fixture.Acme.Orders);
    }

    /// <summary>A panel on its self-signed pair is never ordered a certificate by the job.</summary>
    [Fact]
    public async Task A_panel_on_its_self_signed_pair_is_never_ordered_a_certificate_by_the_job()
    {
        using var fixture = new SslHandlerFixture([]);
        await SeedAsync(fixture, "old.example.net", days: 1);

        var renewed = await HandlerFor(fixture, new StubPanelHostName(PanelName))
            .HandleAsync(new PanelCertificateRenewalRequested(), CancellationToken.None);

        Assert.False(renewed);
        Assert.Empty(fixture.Acme.Orders);
    }

    /// <summary>A failed renewal keeps the certificate on disk and journals the failure.</summary>
    [Fact]
    public async Task A_failed_renewal_keeps_the_certificate_on_disk_and_journals_the_failure()
    {
        using var fixture = new SslHandlerFixture([], acmeFailure: Error.Of("AcmeValidationFailed", ErrorType.UpstreamRefused));
        await SeedAsync(fixture, PanelName, days: 10);

        var renewed = await HandlerFor(fixture, new StubPanelHostName(PanelName))
            .HandleAsync(new PanelCertificateRenewalRequested(), CancellationToken.None);

        Assert.False(renewed);
        Assert.Empty(fixture.Agent.PanelInstalls);
        Assert.Equal(SslHandlerFixture.Now.AddDays(10), (await fixture.DbContext.PanelCertificates.SingleAsync()).ExpiresAt);
        Assert.False(Assert.Single(fixture.Audit.Entries).Succeeded);
        Assert.Equal("AcmeValidationFailed", Assert.Single(fixture.Tasks.Tasks).FailureCode);
    }
}
