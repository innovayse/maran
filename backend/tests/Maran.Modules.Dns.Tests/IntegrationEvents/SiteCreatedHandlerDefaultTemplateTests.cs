using Maran.Modules.Dns.Commands.CreateZone;
using Maran.Modules.Dns.Domain.Enums;
using Maran.Modules.Dns.IntegrationEvents.Handlers;
using Maran.Modules.Dns.Seeders;
using Maran.Modules.Dns.Tests.TestSupport;
using Maran.Sdk.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Maran.Modules.Dns.Tests.IntegrationEvents;

/// <summary>
/// The zone of a new site is made from the default template when one is chosen; a template that cannot be applied
/// falls back to the empty zone with a warning, and any other refusal is not retried. Each has an accepting control.
/// </summary>
public sealed class SiteCreatedHandlerDefaultTemplateTests
{
    /// <summary>The account that creates sites.</summary>
    private static readonly Guid AccountId = new("66666666-6666-4666-8666-666666666666");

    /// <summary>Builds a customer's world with the server on and, optionally, the built-in templates seeded.</summary>
    /// <param name="seeded">Whether the built-in templates and the default are seeded.</param>
    /// <returns>The world.</returns>
    private static async Task<DnsTestContext> WorldAsync(bool seeded)
    {
        using var setup = new DnsTestContext();
        await setup.EnableDnsServerAsync();
        if (seeded)
        {
            await setup.TemplateSeeder.SeedAsync(CancellationToken.None);
        }

        var customer = new DnsTestContext(new CustomerCurrentUser(AccountId), setup.DatabaseName);
        customer.Accounts.Add(AccountId, "erin", maxDnsZones: 3);
        customer.Sites.Claim(AccountId, "shop.example");

        return customer;
    }

    /// <summary>Builds the handler with the real zone creation behind the bus.</summary>
    /// <param name="world">The world.</param>
    /// <param name="bus">The bus, so a test can read what was asked.</param>
    /// <param name="logger">Where the handler reports.</param>
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

    /// <summary>The event for the site.</summary>
    /// <returns>The event.</returns>
    private static SiteCreated Created()
    {
        return new SiteCreated(Guid.NewGuid(), AccountId, "shop.example");
    }

    /// <summary>A new site's zone is made from the default template.</summary>
    [Fact]
    public async Task A_new_site_gets_its_zone_from_the_default_template()
    {
        using var world = await WorldAsync(seeded: true);
        var handler = Handler(world, out var bus, out _);

        await handler.HandleAsync(Created(), CancellationToken.None);

        var asked = Assert.IsType<CreateZoneCommand>(Assert.Single(bus.Invoked));
        Assert.Equal(ZoneTemplateSeeder.WebTemplateId, asked.TemplateId);
        var records = await world.Database.ZoneRecords.AsNoTracking().ToListAsync();
        Assert.Equal(2, records.Count);
        Assert.All(records, record =>
        {
            Assert.Equal(RecordOrigin.Template, record.Origin);
        });
    }

    /// <summary>Control: with no default chosen the zone is empty and no template is named.</summary>
    [Fact]
    public async Task Without_a_default_template_the_zone_is_empty()
    {
        using var world = await WorldAsync(seeded: false);
        var handler = Handler(world, out var bus, out _);

        await handler.HandleAsync(Created(), CancellationToken.None);

        Assert.Null(Assert.IsType<CreateZoneCommand>(Assert.Single(bus.Invoked)).TemplateId);
        Assert.Empty(world.Database.ZoneRecords.AsNoTracking());
        Assert.Single(world.Database.Zones);
    }

    /// <summary>A default that names a template which no longer exists falls back to the empty zone with a warning.</summary>
    [Fact]
    public async Task A_default_template_that_cannot_be_applied_falls_back_to_the_empty_zone()
    {
        using var world = await WorldAsync(seeded: true);
        var settings = await world.Database.DnsSettings.SingleAsync();
        settings.ChooseDefaultTemplate(Guid.NewGuid(), world.Clock.UtcNow);
        await world.Database.SaveChangesAsync();
        var handler = Handler(world, out var bus, out var logger);

        await handler.HandleAsync(Created(), CancellationToken.None);

        Assert.Equal(2, bus.Invoked.Count);
        Assert.Null(((CreateZoneCommand)bus.Invoked[1]).TemplateId);
        Assert.Equal("shop.example", Assert.Single(world.Database.Zones.AsNoTracking()).Name);
        Assert.Empty(world.Database.ZoneRecords.AsNoTracking());
        Assert.Contains(logger.Entries, entry =>
        {
            return entry.Level == LogLevel.Warning && entry.Message.Contains("ZoneTemplateNotFound", StringComparison.Ordinal);
        });
    }

    /// <summary>A refusal that is not the template's is not retried without it.</summary>
    [Fact]
    public async Task A_refusal_that_is_not_the_templates_is_not_retried()
    {
        using var world = await WorldAsync(seeded: true);
        var handler = Handler(world, out var bus, out _);

        await handler.HandleAsync(new SiteCreated(Guid.NewGuid(), AccountId, "unclaimed.example"), CancellationToken.None);

        Assert.Single(bus.Invoked);
        Assert.Empty(world.Database.Zones.AsNoTracking());
    }
}
