using System.Net;
using Maran.Modules.Marketplace.Extensions;
using Maran.Modules.Marketplace.Interfaces;
using Maran.Modules.Marketplace.Models;
using Maran.Modules.Marketplace.Options;
using Maran.Modules.Marketplace.Tests.TestSupport;
using Maran.Sdk.Interfaces;
using Maran.SharedKernel.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Maran.Modules.Marketplace.Tests.Services;

/// <summary>
/// The unit-level half of the outbound gate (plan 4.G, G4 ii and iii; the architecture test holds the
/// source half). With the marketplace off, a whole scripted administrator session sends nothing and never
/// asks for an HTTP client; the same session switched on is shown to reach the wire, which is what proves
/// the counter can see a call. Composing the client's registrations starts no work of its own.
/// </summary>
public sealed class MarketplaceOutboundGateTests
{
    /// <summary>The administrator's session: browse, details, update check, key refresh and a download.</summary>
    /// <param name="world">The test world.</param>
    /// <returns>The failure codes of the five calls, in order.</returns>
    private static async Task<string?[]> Session(MarketplaceTestWorld world)
    {
        var ct = CancellationToken.None;
        var list = await world.Client.ListCatalogueAsync("stable", "en", null, null, ct);
        var details = await world.Client.GetModuleAsync("mail", "en", ct);
        var updates = await world.Client.CheckUpdatesAsync([], "stable", "en", ct);
        var keys = await world.Client.RefreshKeysAsync(ct);
        var download = await world.Client.DownloadPackageAsync("mail", "1.0.0", ct);

        return
        [
            list.Error?.Code,
            details.Error?.Code,
            updates.Error?.Code,
            keys.Error?.Code,
            download.Error?.Code,
        ];
    }

    /// <summary>By default the marketplace is off and a whole session makes no call, creates no client and offers no lease.</summary>
    [Fact]
    public async Task With_the_marketplace_off_a_whole_session_makes_no_outbound_call()
    {
        using var world = new MarketplaceTestWorld();
        world.Switch.Enabled = false;
        world.Lease.HasLease = true;
        world.Handler.Reply(_ => { return new HttpResponseMessage(HttpStatusCode.OK); });

        var codes = await Session(world);

        Assert.All(codes, code => { Assert.Equal("MarketplaceDisabled", code); });
        Assert.Equal(0, world.Handler.Count);
        Assert.Equal(0, world.Factory.CreateCount);
        Assert.Equal(0, world.Lease.Calls);
    }

    /// <summary>The positive control: the same session with the marketplace on reaches the wire, so a count of zero above means something.</summary>
    [Fact]
    public async Task With_the_marketplace_on_the_same_session_reaches_the_wire()
    {
        using var world = new MarketplaceTestWorld();
        world.Switch.Enabled = true;
        world.Handler.Reply(_ => { return new HttpResponseMessage(HttpStatusCode.OK); });

        var codes = await Session(world);

        Assert.DoesNotContain("MarketplaceDisabled", codes);
        Assert.True(world.Handler.Count > 0);
        Assert.True(world.Factory.CreateCount > 0);
    }

    /// <summary>The key status is local: it makes no call even with the marketplace on.</summary>
    [Fact]
    public async Task Reading_the_key_status_makes_no_call_even_when_on()
    {
        using var world = new MarketplaceTestWorld();
        world.Switch.Enabled = true;

        await world.Client.GetKeyStatusAsync(CancellationToken.None);

        Assert.Equal(0, world.Handler.Count);
        Assert.Equal(0, world.Factory.CreateCount);
    }

    /// <summary>
    /// Composing the registrations, building the provider and resolving the client with the marketplace on
    /// registers no hosted service and sends no request: there is no background or start-up path.
    /// </summary>
    [Fact]
    public async Task Composing_the_registrations_with_the_switch_on_starts_no_work_and_sends_nothing()
    {
        var handler = new ScriptedHandler();
        var clock = new AdvancingClock();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IClock>(clock);
        services.AddSingleton<IPanelVersion, FixedPanelVersion>();
        services.AddSingleton<ILeaseRequestAuthorizer, StubLeaseAuthorizer>();
        services.AddSingleton<IOptionsMonitor<MarketplaceOptions>>(
            new TestOptionsMonitor<MarketplaceOptions>(new MarketplaceOptions()));
        services.AddMarketplaceClient();
        services.AddScoped<IMarketplaceSwitch>(_ => { return new StubMarketplaceSwitch { Enabled = true }; });
        services.AddHttpClient(MarketplaceOptions.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => { return handler; });

        await using var provider = services.BuildServiceProvider(validateScopes: true);
        await using var scope = provider.CreateAsyncScope();
        var client = scope.ServiceProvider.GetRequiredService<IMarketplaceClient>();

        Assert.NotNull(client);
        Assert.DoesNotContain(services, descriptor => { return descriptor.ServiceType == typeof(IHostedService); });
        Assert.Equal(0, handler.Count);
    }

    /// <summary>The same composition is shown able to send: once a call is made through it, the counting handler sees it.</summary>
    [Fact]
    public async Task The_composed_client_reaches_the_counting_handler_when_a_call_is_made()
    {
        var handler = new ScriptedHandler();
        handler.Reply(_ => { return new HttpResponseMessage(HttpStatusCode.NotFound); });
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IClock>(new AdvancingClock());
        services.AddSingleton<IPanelVersion, FixedPanelVersion>();
        services.AddSingleton<ILeaseRequestAuthorizer, StubLeaseAuthorizer>();
        services.AddSingleton<IOptionsMonitor<MarketplaceOptions>>(
            new TestOptionsMonitor<MarketplaceOptions>(new MarketplaceOptions()));
        services.AddMarketplaceClient();
        services.AddScoped<IMarketplaceSwitch>(_ => { return new StubMarketplaceSwitch { Enabled = true }; });
        services.AddHttpClient(MarketplaceOptions.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => { return handler; });

        await using var provider = services.BuildServiceProvider(validateScopes: true);
        await using var scope = provider.CreateAsyncScope();
        var client = scope.ServiceProvider.GetRequiredService<IMarketplaceClient>();

        var result = await client.GetModuleAsync("mail", "en", CancellationToken.None);

        Assert.Equal(MarketplaceClientRefusals.ModuleNotInCatalogue, result.Error);
        Assert.Equal(1, handler.Count);
    }
}
