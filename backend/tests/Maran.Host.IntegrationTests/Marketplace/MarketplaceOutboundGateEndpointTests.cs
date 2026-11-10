using System.Net;
using System.Text.Json;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Sdk.Contracts;

namespace Maran.Host.IntegrationTests.Marketplace;

/// <summary>
/// The marketplace is silent until an administrator switches it on, over the panel's real HTTP surface with
/// a counting handler under the named marketplace client: a fresh panel makes no outbound call through a
/// whole administrator session, the same session after the switch makes calls and gets a verified
/// catalogue, the switch needs the password, and switching off closes the path again.
/// </summary>
/// <remarks>
/// <para>
/// The count is taken at the innermost handler of the named client, below the resilience pipeline and the
/// transport, so any request that would reach the network reaches it. The positive control is the same
/// session with the switch on: a count that stayed zero there would mean the probe cannot see requests.
/// </para>
/// <para>
/// UNOBSERVED HERE: a request made through a client the module builds for itself instead of the named one.
/// <c>MarketplaceOutboundIsGatedTests</c> in the architecture tests is what forbids that.
/// </para>
/// </remarks>
[Collection(SharedDatabase.Name)]
public sealed class MarketplaceOutboundGateEndpointTests : IAsyncLifetime
{
    /// <summary>A password that is not the administrator's.</summary>
    private const string WrongPassword = "not the administrator's password";

    /// <summary>This class's own database on the shared server.</summary>
    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public MarketplaceOutboundGateEndpointTests(PostgresFixture postgres)
    {
        _pg = new TestDatabase(postgres);
    }

    /// <inheritdoc />
    public Task InitializeAsync()
    {
        return _pg.CreateAsync();
    }

    /// <inheritdoc />
    public Task DisposeAsync()
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// A freshly started panel makes no outbound call - not while starting, and not through a scripted
    /// administrator session of status, catalogue, module details, installed list and update check - and
    /// the catalogue and update check answer the coded <c>MarketplaceDisabled</c>.
    /// </summary>
    [Fact]
    public async Task A_fresh_panel_makes_no_outbound_call_through_a_scripted_administrator_session()
    {
        await using var world = await MarketplaceWorld.BootAsync(_pg);
        Assert.Equal(0, world.Marketplace.RequestCount);
        using var administrator = await world.SignInAsync("admin");

        var status = await administrator.GetAsync(MarketplaceWorld.ApiRoot + "status");
        var catalogue = await administrator.GetAsync(MarketplaceWorld.ApiRoot + "catalogue");
        var module = await administrator.GetAsync(MarketplaceWorld.ApiRoot + "modules/" + MarketplaceWorld.ModuleId);
        var installed = await administrator.GetAsync(MarketplaceWorld.ApiRoot + "installed");
        var updates = await administrator.GetAsync(MarketplaceWorld.ApiRoot + "updates");

        Assert.Equal(HttpStatusCode.OK, status.Status);
        Assert.False(status.Json().GetProperty("enabled").GetBoolean());
        Assert.True(status.Json().GetProperty("hostConfigured").GetBoolean());
        Assert.Equal(FakeMarketplace.Host, status.Json().GetProperty("host").GetString());
        Assert.Equal((HttpStatusCode.ServiceUnavailable, "MarketplaceDisabled"), (catalogue.Status, catalogue.Code()));
        Assert.Equal((HttpStatusCode.ServiceUnavailable, "MarketplaceDisabled"), (module.Status, module.Code()));
        Assert.Equal(HttpStatusCode.OK, installed.Status);
        Assert.Equal(0, installed.Json().GetArrayLength());
        Assert.Equal((HttpStatusCode.ServiceUnavailable, "MarketplaceDisabled"), (updates.Status, updates.Code()));
        Assert.Equal(0, world.Marketplace.RequestCount);
        Assert.Equal(0, world.Lease.Calls);
    }

    /// <summary>
    /// The positive control for the test above: after the administrator switches the marketplace on with
    /// the right password, the same catalogue and update calls reach the marketplace and return a catalogue
    /// the panel verified against the pinned key, so a zero count above means the gate and not a blind probe.
    /// </summary>
    [Fact]
    public async Task Switching_the_marketplace_on_lets_the_same_calls_reach_it_and_return_a_verified_catalogue()
    {
        await using var world = await MarketplaceWorld.BootAsync(_pg);
        using var administrator = await world.SignInAsync("admin");
        var before = await administrator.GetAsync(MarketplaceWorld.ApiRoot + "catalogue");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, before.Status);
        Assert.Equal(0, world.Marketplace.RequestCount);

        await MarketplaceWorld.EnableAsync(administrator);
        var catalogue = await administrator.GetAsync(MarketplaceWorld.ApiRoot + "catalogue");
        var updates = await administrator.GetAsync(MarketplaceWorld.ApiRoot + "updates");

        Assert.Equal(HttpStatusCode.OK, catalogue.Status);
        var page = catalogue.Json();
        Assert.True(page.GetProperty("verified").GetBoolean());
        var item = Assert.Single(page.GetProperty("items").EnumerateArray().ToList());
        Assert.Equal(MarketplaceWorld.ModuleId, item.GetProperty("id").GetString());
        Assert.Equal("available", item.GetProperty("state").GetString());
        Assert.True(item.GetProperty("canInstall").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, updates.Status);
        Assert.True(updates.Json().GetProperty("verified").GetBoolean());
        Assert.True(world.Marketplace.RequestCount > 0);
        Assert.All(world.Marketplace.Requests, request => { Assert.Equal(FakeMarketplace.Host, request.Host); });
        Assert.Contains(world.Marketplace.Requests, request => { return request.Path == "/api/v1/catalogue"; });
        Assert.Contains(world.Marketplace.Requests, request => { return request.Path == "/api/v1/updates"; });
    }

    /// <summary>
    /// Switching the marketplace on needs the administrator's password: a wrong one is a 400 carrying
    /// <c>ReauthenticationFailed</c> and never a 401 (the SPA signs the user out on a 401), changes nothing
    /// and is audited as a failure; the right one is a 200, switches it on and is audited as a success.
    /// </summary>
    [Fact]
    public async Task Enabling_with_a_wrong_password_is_a_400_never_a_401_and_with_the_right_one_a_200()
    {
        await using var world = await MarketplaceWorld.BootAsync(_pg);
        using var administrator = await world.SignInAsync("admin");

        var wrong = await administrator.SendAsync(
            HttpMethod.Put, MarketplaceWorld.ApiRoot + "settings", new { enabled = true, password = WrongPassword });

        Assert.Equal(HttpStatusCode.BadRequest, wrong.Status);
        Assert.Equal("ReauthenticationFailed", wrong.Code());
        Assert.False(await world.SettingsRowExistsAsync());
        var attempts = await MarketplaceWorld.AuditAsync(administrator, MarketplaceClientAuditActions.MarketplaceEnabled);
        var refused = Assert.Single(attempts);
        Assert.False(refused.GetProperty("succeeded").GetBoolean());
        var stillOff = await administrator.GetAsync(MarketplaceWorld.ApiRoot + "status");
        Assert.False(stillOff.Json().GetProperty("enabled").GetBoolean());
        Assert.Equal(0, world.Marketplace.RequestCount);

        await MarketplaceWorld.EnableAsync(administrator);

        var on = await administrator.GetAsync(MarketplaceWorld.ApiRoot + "status");
        Assert.True(on.Json().GetProperty("enabled").GetBoolean());
        var entries = await MarketplaceWorld.AuditAsync(administrator, MarketplaceClientAuditActions.MarketplaceEnabled);
        Assert.Equal(2, entries.Count);
        Assert.Single(entries, entry => { return entry.GetProperty("succeeded").GetBoolean(); });
    }

    /// <summary>
    /// Switching the marketplace off again closes the outbound path: the catalogue answers
    /// <c>MarketplaceDisabled</c> and the marketplace sees no further request, although the same call a
    /// moment earlier reached it.
    /// </summary>
    [Fact]
    public async Task Switching_the_marketplace_off_again_stops_the_outbound_calls()
    {
        await using var world = await MarketplaceWorld.BootAsync(_pg);
        using var administrator = await world.SignInAsync("admin");
        await MarketplaceWorld.EnableAsync(administrator);
        var reached = await administrator.GetAsync(MarketplaceWorld.ApiRoot + "catalogue");
        Assert.Equal(HttpStatusCode.OK, reached.Status);
        var seen = world.Marketplace.RequestCount;
        Assert.True(seen > 0);

        var off = await administrator.SendAsync(
            HttpMethod.Put,
            MarketplaceWorld.ApiRoot + "settings",
            new { enabled = false, password = EndpointHarness.Password });
        var closed = await administrator.GetAsync(MarketplaceWorld.ApiRoot + "catalogue");
        var installed = await administrator.GetAsync(MarketplaceWorld.ApiRoot + "installed");

        Assert.Equal(HttpStatusCode.OK, off.Status);
        Assert.False(off.Json().GetProperty("enabled").GetBoolean());
        Assert.Equal((HttpStatusCode.ServiceUnavailable, "MarketplaceDisabled"), (closed.Status, closed.Code()));
        Assert.Equal(JsonValueKind.Array, installed.Json().ValueKind);
        Assert.Equal(seen, world.Marketplace.RequestCount);
    }
}
