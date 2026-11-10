using Maran.Modules.Marketplace.Models;
using Maran.Modules.Marketplace.Queries.CheckUpdates;
using Maran.Modules.Marketplace.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Marketplace.Tests.Queries.CheckUpdates;

/// <summary>
/// The update check sends only what the panel itself staged, reports each finding with whether the update can
/// start, remembers withdrawals, and answers an unverifiable report as a 200.
/// </summary>
public sealed class CheckUpdatesQueryHandlerTests
{
    /// <summary>The query as the controller stamps it.</summary>
    private static readonly CheckUpdatesQuery Query = new("203.0.113.5", "test-agent");

    /// <summary>Builds the handler over the world.</summary>
    /// <param name="world">The world.</param>
    /// <returns>The handler.</returns>
    private static CheckUpdatesQueryHandler HandlerFor(OperationWorld world)
    {
        return new CheckUpdatesQueryHandler(
            world.Client, world.Installed, world.OptionsReader, world.Eligibility, world.Revocations, world.Names, world.Journal);
    }

    /// <summary>Builds a verified report.</summary>
    /// <param name="notices">What the service found.</param>
    /// <returns>The report.</returns>
    private static Result<UpdatesReport> Report(params ModuleUpdateNotice[] notices)
    {
        var signed = new DateTimeOffset(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);
        return Result<UpdatesReport>.Ok(new UpdatesReport(notices, signed, signed.AddHours(24)));
    }

    /// <summary>Only modules the panel staged are sent, with id, version and digest and nothing else; a module that arrived another way is left out.</summary>
    [Fact]
    public async Task Only_modules_the_panel_staged_are_sent()
    {
        using var world = new OperationWorld();
        await world.Ledger.RecordStagedAsync("mail", "1.0.0", CatalogueSamples.Digest, world.UserId, CancellationToken.None);
        world.Loader.Seed("mail", "1.0.0", ModuleLifecycleState.Active);
        world.Loader.Seed("sideloaded", "2.0.0", ModuleLifecycleState.Active);
        world.Client.Updates = Report();

        await HandlerFor(world).HandleAsync(Query, CancellationToken.None);

        var sent = Assert.Single(world.Client.LastReferences);
        Assert.Equal(new InstalledModuleReference("mail", "1.0.0", CatalogueSamples.Digest), sent);
    }

    /// <summary>A newer version the panel may take is reported as updatable; one it may not (incompatible with this panel) says why.</summary>
    [Fact]
    public async Task A_newer_version_is_updatable_and_a_blocked_one_says_why()
    {
        using var world = new OperationWorld();
        await world.Ledger.RecordStagedAsync("mail", "1.0.0", CatalogueSamples.Digest, world.UserId, CancellationToken.None);
        await world.Ledger.RecordStagedAsync("dns", "1.0.0", CatalogueSamples.Digest, world.UserId, CancellationToken.None);
        world.Loader.Seed("mail", "1.0.0", ModuleLifecycleState.Active);
        world.Loader.Seed("dns", "1.0.0", ModuleLifecycleState.Active);
        world.Client.Updates = Report(
            new ModuleUpdateNotice("mail", Revoked: false, CatalogueSamples.Version("1.1.0")),
            new ModuleUpdateNotice("dns", Revoked: false, CatalogueSamples.Version("2.0.0", minPanelVersion: "2.0.0")));

        var result = await HandlerFor(world).HandleAsync(Query, CancellationToken.None);

        Assert.True(result.Value.Verified);
        var mail = result.Value.Items.Single(item => { return item.Id == "mail"; });
        var dns = result.Value.Items.Single(item => { return item.Id == "dns"; });
        Assert.True(mail.CanUpdate);
        Assert.Equal("1.1.0", mail.AvailableVersion);
        Assert.Null(mail.CannotUpdateMessage);
        Assert.False(dns.CanUpdate);
        Assert.Equal(EchoErrorTextProvider.Prefix + ErrorCodes.IncompatiblePanel, dns.CannotUpdateMessage);
    }

    /// <summary>A module the service withdrew is reported revoked, cannot be updated, and is remembered for the installed list.</summary>
    [Fact]
    public async Task A_withdrawn_module_is_reported_and_remembered()
    {
        using var world = new OperationWorld();
        await world.Ledger.RecordStagedAsync("mail", "1.0.0", CatalogueSamples.Digest, world.UserId, CancellationToken.None);
        world.Loader.Seed("mail", "1.0.0", ModuleLifecycleState.Active);
        world.Client.Updates = Report(new ModuleUpdateNotice("mail", Revoked: true, Latest: null, RevokedReason: "Unsafe"));

        var result = await HandlerFor(world).HandleAsync(Query, CancellationToken.None);

        var mail = Assert.Single(result.Value.Items);
        Assert.True(mail.Revoked);
        Assert.Equal("Unsafe", mail.RevokedMessage);
        Assert.False(mail.CanUpdate);
        Assert.True(world.Revocations.IsRevoked("mail"));
    }

    /// <summary>A module with no newer version says nothing about why it cannot be updated.</summary>
    [Fact]
    public async Task An_up_to_date_module_has_no_reason()
    {
        using var world = new OperationWorld();
        await world.Ledger.RecordStagedAsync("mail", "1.0.0", CatalogueSamples.Digest, world.UserId, CancellationToken.None);
        world.Loader.Seed("mail", "1.0.0", ModuleLifecycleState.Active);
        world.Client.Updates = Report(new ModuleUpdateNotice("mail", Revoked: false, Latest: null));

        var result = await HandlerFor(world).HandleAsync(Query, CancellationToken.None);

        var mail = Assert.Single(result.Value.Items);
        Assert.False(mail.CanUpdate);
        Assert.Null(mail.AvailableVersion);
        Assert.Null(mail.CannotUpdateMessage);
    }

    /// <summary>A report whose signature failed is a 200 with verified false and a journalled failure; an outage is the request failing.</summary>
    [Fact]
    public async Task An_unverifiable_report_is_an_answer_and_an_outage_is_an_error()
    {
        using var world = new OperationWorld();
        world.Client.Updates = Result<UpdatesReport>.Fail(MarketplaceClientRefusals.CatalogueStale);

        var unverified = await HandlerFor(world).HandleAsync(Query, CancellationToken.None);

        Assert.True(unverified.IsSuccess);
        Assert.False(unverified.Value.Verified);
        Assert.Empty(unverified.Value.Items);
        Assert.Equal(EchoErrorTextProvider.Prefix + ErrorCodes.CatalogueStale, unverified.Value.UnverifiedMessage);
        Assert.Single(world.AuditFor(MarketplaceClientAuditActions.CatalogueVerificationFailed));

        world.Client.Updates = Result<UpdatesReport>.Fail(MarketplaceClientRefusals.Unreachable);
        var outage = await HandlerFor(world).HandleAsync(Query, CancellationToken.None);

        Assert.Equal(ErrorCodes.MarketplaceUnreachable, outage.Error!.Code);
    }
}
