using Maran.Modules.Dns.Commands.CreateZone;
using Maran.Modules.Dns.IntegrationEvents.Handlers;
using Maran.Modules.Dns.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.Sdk.Events;
using Microsoft.Extensions.Logging;

namespace Maran.Modules.Dns.Tests.IntegrationEvents;

/// <summary>
/// A new site gets its zone when this server publishes DNS, the plan sells zones and nothing already covers
/// the name; otherwise nothing happens and nothing is audited. Each refusal-to-act test has an accepting
/// control, so a handler that never created a zone cannot pass them all.
/// </summary>
public sealed class SiteCreatedHandlerTests
{
    /// <summary>The account that creates sites.</summary>
    private static readonly Guid AccountId = new("55555555-5555-4555-8555-555555555555");

    /// <summary>Builds a customer's world for the account, the server on or off, with an allowance of zones.</summary>
    /// <param name="maxDnsZones">How many zones the plan allows.</param>
    /// <param name="dnsOn">Whether the administrator switched the name server on.</param>
    /// <returns>The world.</returns>
    private static async Task<DnsTestContext> WorldAsync(int maxDnsZones = 3, bool dnsOn = true)
    {
        using var setup = new DnsTestContext();
        if (dnsOn)
        {
            await setup.EnableDnsServerAsync();
        }

        var customer = new DnsTestContext(new CustomerCurrentUser(AccountId), setup.DatabaseName);
        customer.Accounts.Add(AccountId, "erin", maxDnsZones);

        return customer;
    }

    /// <summary>Builds the handler over a world, with the real zone creation behind the bus.</summary>
    /// <param name="world">The world.</param>
    /// <param name="bus">The bus it invokes the creation on, so a test can read what was asked.</param>
    /// <param name="logger">Where it reports.</param>
    /// <returns>The handler.</returns>
    private static SiteCreatedHandler Handler(
        DnsTestContext world,
        out DispatchingMessageBus bus,
        out RecordingLogger<SiteCreatedHandler> logger)
    {
        bus = new DispatchingMessageBus(async (message, cancellationToken) =>
        {
            return await world.CreateZone.HandleAsync((CreateZoneCommand)message, cancellationToken);
        });
        logger = new RecordingLogger<SiteCreatedHandler>();

        return new SiteCreatedHandler(world.Database, world.Accounts, bus, logger);
    }

    /// <summary>Builds the event for a domain.</summary>
    /// <param name="domain">The site's primary domain.</param>
    /// <returns>The event.</returns>
    private static SiteCreated Created(string domain)
    {
        return new SiteCreated(Guid.NewGuid(), AccountId, domain);
    }

    /// <summary>A new site gets a zone named for its domain, served and recorded, and journalled as a creation.</summary>
    [Fact]
    public async Task A_new_site_gets_a_zone_named_for_its_domain()
    {
        using var world = await WorldAsync();
        world.Sites.Claim(AccountId, "shop.example");
        var handler = Handler(world, out var bus, out _);

        await handler.HandleAsync(Created("Shop.Example"), CancellationToken.None);

        Assert.Single(bus.Invoked);
        Assert.Equal("shop.example", Assert.Single(world.Agent.Puts).Name);
        Assert.Equal("shop.example", Assert.Single(world.Database.Zones).Name);
        Assert.Equal(AccountId, world.Database.Zones.Single().AccountId);
        var entry = Assert.Single(world.Entries);
        Assert.Equal(DnsAuditActions.DnsZoneCreated, entry.Action);
        Assert.True(entry.Succeeded);
    }

    /// <summary>With the name server off nothing is asked, served or audited.</summary>
    [Fact]
    public async Task Nothing_happens_while_the_name_server_is_off()
    {
        using var world = await WorldAsync(dnsOn: false);
        world.Sites.Claim(AccountId, "shop.example");
        var handler = Handler(world, out var bus, out _);

        await handler.HandleAsync(Created("shop.example"), CancellationToken.None);

        Assert.Empty(bus.Invoked);
        Assert.Empty(world.Agent.Puts);
        Assert.Empty(world.Entries);
    }

    /// <summary>A plan that sells no zone gets none, and no refused creation is audited for it.</summary>
    [Fact]
    public async Task A_plan_without_zones_gets_none()
    {
        using var world = await WorldAsync(maxDnsZones: 0);
        world.Sites.Claim(AccountId, "shop.example");
        var handler = Handler(world, out var bus, out _);

        await handler.HandleAsync(Created("shop.example"), CancellationToken.None);

        Assert.Empty(bus.Invoked);
        Assert.Empty(world.Entries);
    }

    /// <summary>An account that has used its allowance gets no further zone, and no refused creation is audited.</summary>
    [Fact]
    public async Task An_account_that_has_used_its_allowance_gets_no_further_zone()
    {
        using var world = await WorldAsync(maxDnsZones: 1);
        world.Sites.Claim(AccountId, "first.example");
        world.Sites.Claim(AccountId, "second.example");
        var handler = Handler(world, out var bus, out var logger);
        await handler.HandleAsync(Created("first.example"), CancellationToken.None);

        await handler.HandleAsync(Created("second.example"), CancellationToken.None);

        Assert.Single(bus.Invoked);
        Assert.Equal("first.example", Assert.Single(world.Database.Zones).Name);
        Assert.Single(world.Entries);
        Assert.Contains(logger.Entries, entry =>
        {
            return entry.Level == LogLevel.Information;
        });
    }

    /// <summary>A site inside a zone the account already owns gets no zone of its own, which would cut it out of the parent.</summary>
    [Fact]
    public async Task A_site_inside_an_owned_zone_gets_no_zone_of_its_own()
    {
        using var world = await WorldAsync();
        world.Sites.Claim(AccountId, "example.com");
        world.Sites.Claim(AccountId, "blog.example.com");
        var handler = Handler(world, out var bus, out _);
        await handler.HandleAsync(Created("example.com"), CancellationToken.None);

        await handler.HandleAsync(Created("blog.example.com"), CancellationToken.None);

        Assert.Single(bus.Invoked);
        Assert.Equal("example.com", Assert.Single(world.Database.Zones).Name);
    }

    /// <summary>A name that merely ends with an owned zone's letters is not inside it, and gets its own zone.</summary>
    [Fact]
    public async Task A_name_that_only_ends_with_the_same_letters_is_not_inside_an_owned_zone()
    {
        // The control for the test above: the check is on a label boundary, not on a suffix of letters.
        using var world = await WorldAsync();
        world.Sites.Claim(AccountId, "example.com");
        world.Sites.Claim(AccountId, "notexample.com");
        var handler = Handler(world, out var bus, out _);
        await handler.HandleAsync(Created("example.com"), CancellationToken.None);

        await handler.HandleAsync(Created("notexample.com"), CancellationToken.None);

        Assert.Equal(2, bus.Invoked.Count);
        Assert.Equal(["example.com", "notexample.com"], world.Database.Zones.Select(zone => zone.Name).Order().ToList());
    }

    /// <summary>A zone the account already owns for the name is not created a second time.</summary>
    [Fact]
    public async Task A_name_the_account_already_owns_is_not_created_again()
    {
        using var world = await WorldAsync();
        world.Sites.Claim(AccountId, "shop.example");
        var handler = Handler(world, out var bus, out _);
        await handler.HandleAsync(Created("shop.example"), CancellationToken.None);

        await handler.HandleAsync(Created("shop.example"), CancellationToken.None);

        Assert.Single(bus.Invoked);
        Assert.Single(world.Entries);
    }

    /// <summary>A primary domain that cannot be a zone apex is skipped.</summary>
    [Fact]
    public async Task A_domain_that_cannot_be_a_zone_apex_is_skipped()
    {
        using var world = await WorldAsync();
        world.Sites.Claim(AccountId, "localhost");
        var handler = Handler(world, out var bus, out var logger);

        await handler.HandleAsync(Created("localhost"), CancellationToken.None);

        Assert.Empty(bus.Invoked);
        Assert.Contains(logger.Entries, entry =>
        {
            return entry.Level == LogLevel.Information;
        });
    }

    /// <summary>An account the caller cannot read gets no zone.</summary>
    [Fact]
    public async Task An_account_the_caller_cannot_read_gets_no_zone()
    {
        using var world = await WorldAsync();
        world.Sites.Claim(AccountId, "shop.example");
        var handler = Handler(world, out var bus, out _);

        await handler.HandleAsync(new SiteCreated(Guid.NewGuid(), Guid.NewGuid(), "shop.example"), CancellationToken.None);

        Assert.Empty(bus.Invoked);
        Assert.Empty(world.Database.Zones);
    }

    /// <summary>
    /// A creation the command refuses is logged as a warning and does not throw, because the site exists
    /// whatever happens to its zone. Here the refusal is the claim a customer's zone must have.
    /// </summary>
    [Fact]
    public async Task A_refusal_by_the_creation_is_logged_and_does_not_throw()
    {
        using var world = await WorldAsync();
        var handler = Handler(world, out var bus, out var logger);

        await handler.HandleAsync(Created("unclaimed.example"), CancellationToken.None);

        Assert.Single(bus.Invoked);
        Assert.Empty(world.Database.Zones);
        Assert.Contains(logger.Entries, entry =>
        {
            return entry.Level == LogLevel.Warning && entry.Message.Contains("ZoneApexNotClaimed", StringComparison.Ordinal);
        });
    }
}
