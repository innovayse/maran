using Maran.Host.BackgroundServices;
using Maran.Modules.Ssl.Common.Options;
using Maran.Modules.Ssl.Jobs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Wolverine.Runtime.Handlers;

namespace Maran.Host.Tests.Composition;

/// <summary>
/// The composition the renewal job depends on to run at all: something that publishes its trigger on
/// a schedule, and an ACME client whose configured timeout is the one that applies.
/// </summary>
/// <remarks>
/// Both are the kind of defect no other test can see. A renewal job with no schedule is fully
/// implemented, fully tested, and never runs — every certificate it issues expires ninety days later
/// with nothing watching, and the whole suite stays green because what is missing is exactly the
/// wiring. Likewise a per-attempt timeout wrapped in an identical outer one is documented behaviour
/// that cannot occur.
/// </remarks>
public sealed class CertificateRenewalSchedulingTests : IClassFixture<PanelTestFactory>
{
    /// <summary>The shared in-memory host factory.</summary>
    private readonly PanelTestFactory _factory;

    /// <summary>Captures the shared in-memory host factory.</summary>
    /// <param name="factory">The booted host.</param>
    public CertificateRenewalSchedulingTests(PanelTestFactory factory)
    {
        _factory = factory;
    }

    /// <summary>Something in the host publishes the renewal trigger on a schedule.</summary>
    [Fact]
    public void Something_in_the_host_publishes_the_renewal_trigger_on_a_schedule()
    {
        var hosted = _factory.Services.GetServices<IHostedService>();

        Assert.Contains(hosted, service =>
        {
            return service is CertificateRenewalScheduler;
        });
    }

    /// <summary>The published renewal trigger is routed to a handler the running panel discovered.</summary>
    /// <remarks>
    /// Asked of the RUNNING message bus, not of a registration. Publishing the trigger to a message
    /// nothing handles is not an error and not an exception: Wolverine writes
    /// <c>No routes can be determined for Envelope …</c> to the log and drops it, so the scheduler
    /// above ran daily for the life of the panel while renewal never happened once. The one thing
    /// that made it so was the handler type being named <c>…Job</c> — Wolverine discovers
    /// <c>…Handler</c> and <c>…Consumer</c> and nothing else — and no test in the suite could see
    /// it, because every renewal test constructs the type by hand and calls its method directly.
    /// </remarks>
    [Fact]
    public void The_published_renewal_trigger_is_routed_to_a_handler_the_running_panel_discovered()
    {
        var handlers = _factory.Services.GetRequiredService<HandlerGraph>();

        Assert.True(
            handlers.CanHandle(typeof(CertificateRenewalRequested)),
            "the running panel has no handler for CertificateRenewalRequested, so every daily "
                + "publish is dropped with 'No routes can be determined' and no certificate is renewed");
    }

    /// <summary>The handler the renewal trigger is routed to resolves from the panel's own container.</summary>
    /// <remarks>
    /// Discovery and construction are two separate ways to fail, and each is silent on its own. A
    /// discovered handler whose dependencies the container cannot supply throws at the moment the
    /// message arrives — inside a background publish nobody is awaiting — so it fails exactly where
    /// no operator is looking.
    /// </remarks>
    [Fact]
    public void The_handler_the_renewal_trigger_is_routed_to_resolves_from_the_panels_own_container()
    {
        using var scope = _factory.Services.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<CertificateRenewalHandler>());
    }

    /// <summary>The renewal cadence is daily which is what the thirty day window is designed around.</summary>
    [Fact]
    public void The_renewal_cadence_is_daily_which_is_what_the_thirty_day_window_is_designed_around()
    {
        // A ninety-day certificate entering a thirty-day window gets thirty opportunities before it
        // expires. Weekly would give four, which a single bad week consumes.
        Assert.Equal(TimeSpan.FromDays(1), CertificateRenewalScheduler.Interval);
    }

    /// <summary>The first pass waits for startup to settle rather than running on every boot.</summary>
    [Fact]
    public void The_first_pass_waits_for_startup_to_settle_rather_than_running_on_every_boot()
    {
        // A panel in a crash loop would otherwise start a renewal pass on every boot, and every pass
        // spends orders at a shared, rate-limited account.
        Assert.True(CertificateRenewalScheduler.StartupDelay > TimeSpan.Zero);
    }

    /// <summary>The acme client leaves its deadline to the resilience pipeline.</summary>
    [Fact]
    public void The_acme_client_leaves_its_deadline_to_the_resilience_pipeline()
    {
        // An HttpClient.Timeout equal to the pipeline's per-attempt timeout makes the per-attempt
        // budget unreachable: the outer deadline expires while the first attempt is still using it.
        using var scope = _factory.Services.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<IHttpClientFactory>();

        using var client = factory.CreateClient(AcmeOptions.HttpClientName);

        Assert.Equal(Timeout.InfiniteTimeSpan, client.Timeout);
    }
}
