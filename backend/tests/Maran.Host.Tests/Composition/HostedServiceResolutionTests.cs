using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Maran.Host.Tests.Composition;

/// <summary>
/// The composition root, asked the question a real boot asks: does the container actually build, and
/// can it hand out every background service the panel registers.
/// </summary>
/// <remarks>
/// This suite exists because its absence let the panel become unable to start while 690 tests
/// passed. <c>CertificateRenewalScheduler</c> is a <see cref="BackgroundService"/> — a singleton —
/// and it took Wolverine's scoped <c>IMessageBus</c> in its constructor. The container refuses that
/// combination at build time, so <c>WebApplicationBuilder.Build()</c> threw and the whole API failed
/// to start, deterministically, on every boot. Not a degraded feature: no HTTP surface at all.
///
/// Every one of those 690 tests constructed what it needed directly, and the two that touch the
/// container did so through a factory booting as "Testing", where ASP.NET Core turns
/// <c>ValidateOnBuild</c> and <c>ValidateScopes</c> OFF. So the suite proved every piece worked and
/// never asked whether the thing that assembles them does — the same shape as the Singleton
/// <c>DbContext</c> that passed 452 tests before
/// <see cref="ContainerResolutionTests.Every_module_database_context_is_scoped_to_one_request"/> was
/// written.
///
/// A hosted service is where this bites hardest, because it is the one kind of registration nothing
/// else exercises: nothing resolves it, no request reaches it, and its only consumer is the host
/// itself at startup.
/// </remarks>
public sealed class HostedServiceResolutionTests : IClassFixture<ValidatingPanelTestFactory>
{
    /// <summary>The host booted with a real boot's container validation.</summary>
    private readonly ValidatingPanelTestFactory _factory;

    /// <summary>Captures the validating host factory.</summary>
    /// <param name="factory">The booted host.</param>
    public HostedServiceResolutionTests(ValidatingPanelTestFactory factory)
    {
        _factory = factory;
    }

    /// <summary>The container builds under the validation a real boot performs.</summary>
    [Fact]
    public void The_container_builds_under_the_validation_a_real_boot_performs()
    {
        // Touching Services is what forces the build, and the build is the assertion: a singleton
        // that captures a scoped dependency throws here with the offending pair named.
        Assert.NotNull(_factory.Services);
    }

    /// <summary>Every registered hosted service resolves from the root provider.</summary>
    /// <remarks>
    /// From the ROOT provider deliberately, because that is where the host resolves them. Resolving
    /// inside a scope would succeed for a service that captures a scoped dependency and prove
    /// nothing — the mistake only shows where the real consumer stands.
    /// </remarks>
    [Fact]
    public void Every_registered_hosted_service_resolves_from_the_root_provider()
    {
        var hosted = _factory.Services.GetServices<IHostedService>().ToList();

        Assert.NotEmpty(hosted);
        Assert.All(hosted, service =>
        {
            Assert.NotNull(service);
        });
    }

    /// <summary>The host starts and answers a request.</summary>
    /// <remarks>
    /// The end of the same question. Building the container is necessary and not sufficient: a
    /// hosted service whose constructor is fine can still be registered in a way that fails when the
    /// host actually starts it, and nothing short of starting the host would notice.
    /// </remarks>
    [Fact]
    public async Task The_host_starts_and_answers_a_request()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/modules");

        Assert.True(response.IsSuccessStatusCode);
    }
}
