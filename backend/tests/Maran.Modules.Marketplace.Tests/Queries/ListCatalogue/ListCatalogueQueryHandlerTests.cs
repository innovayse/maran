using Maran.Modules.Marketplace.Domain.Enums;
using Maran.Modules.Marketplace.Models;
using Maran.Modules.Marketplace.Queries.ListCatalogue;
using Maran.Modules.Marketplace.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Marketplace.Tests.Queries.ListCatalogue;

/// <summary>
/// The catalogue list shows each module with what it may do on this panel, answers an unverifiable
/// catalogue as a 200 the page can still render, and fails with a code when the marketplace is off or out
/// of reach.
/// </summary>
public sealed class ListCatalogueQueryHandlerTests
{
    /// <summary>The query as the controller stamps it.</summary>
    private static readonly ListCatalogueQuery Query = new(IpAddress: "203.0.113.5", UserAgent: "test-agent");

    /// <summary>Builds the handler over the world.</summary>
    /// <param name="world">The world.</param>
    /// <returns>The handler.</returns>
    private static ListCatalogueQueryHandler HandlerFor(OperationWorld world)
    {
        return new ListCatalogueQueryHandler(
            world.Client, world.OptionsReader, world.Eligibility, world.Projector, world.Names, world.Journal);
    }

    /// <summary>A revoked module is listed as revoked and is not installable, with the reason in words; a live one beside it is offered.</summary>
    [Fact]
    public async Task A_revoked_version_is_not_offered()
    {
        using var world = new OperationWorld();
        world.Client.Page = Result<CataloguePage>.Ok(CatalogueSamples.Page(
            CatalogueSamples.Module("mail", CatalogueSamples.Version("1.0.0", revoked: true)),
            CatalogueSamples.Module("dns", CatalogueSamples.Version("1.0.0"))));

        var catalogue = await HandlerFor(world).HandleAsync(Query, CancellationToken.None);

        Assert.True(catalogue.Value.Verified);
        var mail = catalogue.Value.Items.Single(item => { return item.Id == "mail"; });
        var dns = catalogue.Value.Items.Single(item => { return item.Id == "dns"; });
        Assert.Equal(MarketplaceModuleState.Revoked, mail.State);
        Assert.False(mail.CanInstall);
        Assert.Equal(EchoErrorTextProvider.Prefix + ErrorCodes.PackageRevoked, mail.CannotInstallMessage);
        Assert.True(dns.CanInstall);
        Assert.Null(dns.CannotInstallMessage);
        Assert.Equal(MarketplaceModuleState.Available, dns.State);
    }

    /// <summary>A version needing a newer panel is incompatible and not installable; one that fits this panel is installable.</summary>
    [Fact]
    public async Task An_incompatible_panel_version_is_not_installable()
    {
        using var world = new OperationWorld();
        world.Client.Page = Result<CataloguePage>.Ok(CatalogueSamples.Page(
            CatalogueSamples.Module("mail", CatalogueSamples.Version("2.0.0", minPanelVersion: "2.0.0")),
            CatalogueSamples.Module("dns", CatalogueSamples.Version("1.0.0", minPanelVersion: "1.0.0", maxPanelVersion: "1.9.0")),
            CatalogueSamples.Module("ftp", CatalogueSamples.Version("1.0.0", minPanelVersion: "1.0.0", maxPanelVersion: "1.3.0"))));

        var catalogue = await HandlerFor(world).HandleAsync(Query, CancellationToken.None);

        var byId = catalogue.Value.Items.ToDictionary(item => { return item.Id; });
        Assert.Equal(MarketplaceModuleState.Incompatible, byId["mail"].State);
        Assert.False(byId["mail"].CanInstall);
        Assert.True(byId["dns"].CanInstall);
        Assert.Equal(MarketplaceModuleState.Incompatible, byId["ftp"].State);
        Assert.Equal(EchoErrorTextProvider.Prefix + ErrorCodes.IncompatiblePanel, byId["ftp"].CannotInstallMessage);
    }

    /// <summary>Without a lease every module is listed and none is installable; an entitled panel is offered the same modules.</summary>
    [Fact]
    public async Task Without_a_lease_the_catalogue_is_browse_only()
    {
        using var world = new OperationWorld();
        world.Client.Page = Result<CataloguePage>.Ok(CatalogueSamples.Page(
            CatalogueSamples.Module("mail", CatalogueSamples.Version("1.0.0"), EntitlementState.Unknown),
            CatalogueSamples.Module("dns", CatalogueSamples.Version("1.0.0"), EntitlementState.NotPurchased)));

        var browseOnly = await HandlerFor(world).HandleAsync(Query, CancellationToken.None);

        Assert.Equal(2, browseOnly.Value.Items.Count);
        Assert.All(browseOnly.Value.Items, item => { Assert.False(item.CanInstall); });
        Assert.Equal(EchoErrorTextProvider.Prefix + ErrorCodes.LeaseRequired, browseOnly.Value.Items[0].CannotInstallMessage);
        Assert.Equal(EntitlementState.Unknown, browseOnly.Value.Items[0].Entitlement.State);

        world.Client.Page = Result<CataloguePage>.Ok(CatalogueSamples.Page(
            CatalogueSamples.Module("mail", CatalogueSamples.Version("1.0.0"), EntitlementState.Entitled),
            CatalogueSamples.Module("dns", CatalogueSamples.Version("1.0.0"), EntitlementState.NotRequired)));
        var entitled = await HandlerFor(world).HandleAsync(Query, CancellationToken.None);

        Assert.All(entitled.Value.Items, item => { Assert.True(item.CanInstall); });
    }

    /// <summary>A version below the ledger floor is listed but not installable; a newer one is.</summary>
    [Fact]
    public async Task The_ledger_floor_blocks_an_older_catalogue_version()
    {
        using var world = new OperationWorld();
        await world.Ledger.RecordStagedAsync("mail", "2.0.0", CatalogueSamples.Digest, world.UserId, CancellationToken.None);
        await world.Ledger.RecordRemovalAsync("mail", CancellationToken.None);
        world.Client.Page = Result<CataloguePage>.Ok(CatalogueSamples.Page(
            CatalogueSamples.Module("mail", CatalogueSamples.Version("1.5.0")),
            CatalogueSamples.Module("dns", CatalogueSamples.Version("1.5.0"))));

        var older = await HandlerFor(world).HandleAsync(Query, CancellationToken.None);
        world.Client.Page = Result<CataloguePage>.Ok(CatalogueSamples.Page(
            CatalogueSamples.Module("mail", CatalogueSamples.Version("2.1.0"))));
        var newer = await HandlerFor(world).HandleAsync(Query, CancellationToken.None);

        var blocked = older.Value.Items.Single(item => { return item.Id == "mail"; });
        Assert.False(blocked.CanInstall);
        Assert.Equal(EchoErrorTextProvider.Prefix + ErrorCodes.ModuleVersionNotInstallable, blocked.CannotInstallMessage);
        Assert.True(older.Value.Items.Single(item => { return item.Id == "dns"; }).CanInstall);
        Assert.True(newer.Value.Items.Single().CanInstall);
    }

    /// <summary>An installed module shows its installed version, update-available when the catalogue is ahead, and a restart-required flag when staged.</summary>
    [Fact]
    public async Task An_installed_module_shows_its_version_and_state()
    {
        using var world = new OperationWorld();
        world.Loader.Seed("mail", "1.0.0", ModuleLifecycleState.Active);
        world.Loader.Seed("dns", "1.0.0", ModuleLifecycleState.Installed);
        world.Client.Page = Result<CataloguePage>.Ok(CatalogueSamples.Page(
            CatalogueSamples.Module("mail", CatalogueSamples.Version("1.1.0")),
            CatalogueSamples.Module("dns", CatalogueSamples.Version("1.0.0"))));

        var catalogue = await HandlerFor(world).HandleAsync(Query, CancellationToken.None);

        var mail = catalogue.Value.Items.Single(item => { return item.Id == "mail"; });
        var dns = catalogue.Value.Items.Single(item => { return item.Id == "dns"; });
        Assert.Equal("1.0.0", mail.InstalledVersion);
        Assert.Equal(MarketplaceModuleState.UpdateAvailable, mail.State);
        Assert.True(mail.CanInstall);
        Assert.False(mail.RestartRequired);
        Assert.Equal(MarketplaceModuleState.Installed, dns.State);
        Assert.True(dns.RestartRequired);
        Assert.False(dns.CanInstall);
    }

    /// <summary>A catalogue whose signature or freshness failed is a 200 with verified false, no items and a sentence; the failure is journalled; an outage is the request failing.</summary>
    [Fact]
    public async Task An_unverifiable_catalogue_is_an_answer_and_an_outage_is_an_error()
    {
        using var world = new OperationWorld();
        world.Client.Page = Result<CataloguePage>.Fail(MarketplaceClientRefusals.CatalogueUnsigned);

        var unverified = await HandlerFor(world).HandleAsync(Query, CancellationToken.None);

        Assert.True(unverified.IsSuccess);
        Assert.False(unverified.Value.Verified);
        Assert.Empty(unverified.Value.Items);
        Assert.Null(unverified.Value.FetchedAt);
        Assert.Equal(EchoErrorTextProvider.Prefix + ErrorCodes.CatalogueUnsigned, unverified.Value.UnverifiedMessage);
        var entry = Assert.Single(world.AuditFor(MarketplaceClientAuditActions.CatalogueVerificationFailed));
        Assert.False(entry.Succeeded);

        world.Client.Page = Result<CataloguePage>.Fail(MarketplaceClientRefusals.Unreachable);
        var outage = await HandlerFor(world).HandleAsync(Query, CancellationToken.None);

        Assert.Equal(ErrorCodes.MarketplaceUnreachable, outage.Error!.Code);
        Assert.Single(world.AuditFor(MarketplaceClientAuditActions.CatalogueVerificationFailed));
    }

    /// <summary>A switched-off marketplace answers with its own code and the page is empty.</summary>
    [Fact]
    public async Task A_switched_off_marketplace_is_refused_with_its_own_code()
    {
        using var world = new OperationWorld();
        world.Client.Page = Result<CataloguePage>.Fail(MarketplaceClientRefusals.Disabled);

        var refused = await HandlerFor(world).HandleAsync(Query, CancellationToken.None);

        Assert.Equal(ErrorCodes.MarketplaceDisabled, refused.Error!.Code);
        Assert.Equal(ErrorType.Unavailable, refused.Error.Type);
    }
}
