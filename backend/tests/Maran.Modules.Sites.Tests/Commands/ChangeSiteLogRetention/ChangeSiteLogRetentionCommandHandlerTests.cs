using Maran.Modules.Sites.Commands.ChangeSiteLogRetention;
using Maran.Modules.Sites.IntegrationEvents.Events;
using Maran.Modules.Sites.Persistence;
using Maran.Modules.Sites.Services;
using Maran.Modules.Sites.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Microsoft.EntityFrameworkCore;
using Wolverine;

namespace Maran.Modules.Sites.Tests.Commands.ChangeSiteLogRetention;

/// <summary>Behavioral contract of <see cref="ChangeSiteLogRetentionCommandHandler"/>.</summary>
public sealed class ChangeSiteLogRetentionCommandHandlerTests
{
    /// <summary>Changing the retention stores it, and it survives into a fresh context.</summary>
    [Fact]
    public async Task Changing_the_retention_stores_it_and_it_survives_into_a_fresh_context()
    {
        var account = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        var siteId = await SeedAsync(database, account);
        await using (var context = SitesTestContext.Create(FakeCurrentUser.Customer(account), database))
        {
            var result = await Handler(context, account, new RecordingMessageBus()).HandleAsync(
                new ChangeSiteLogRetentionCommand(5, siteId, "198.51.100.7", "tests"),
                CancellationToken.None);

            Assert.True(result.IsSuccess);
            Assert.Equal(5, result.Value.LogRetentionDays);
            Assert.Equal("acme", result.Value.OwnerName);
        }

        await using var reader = SitesTestContext.Create(FakeCurrentUser.Admin(), database);
        Assert.Equal(5, (await reader.Sites.SingleAsync()).LogRetentionDays);
    }

    /// <summary>The change is journalled with the domain as the subject, and the caller's address.</summary>
    [Fact]
    public async Task The_change_is_journalled_with_the_domain_as_the_subject()
    {
        var account = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        var siteId = await SeedAsync(database, account);
        var audit = new RecordingAuditWriter();
        await using var context = SitesTestContext.Create(FakeCurrentUser.Customer(account), database);

        await Handler(context, account, new RecordingMessageBus(), audit).HandleAsync(
            new ChangeSiteLogRetentionCommand(5, siteId, "198.51.100.7", "tests"),
            CancellationToken.None);

        var entry = Assert.Single(audit.Entries);
        Assert.Equal(SiteAuditActions.SiteLogRetentionChanged, entry.Action);
        Assert.Equal("example.com", entry.Subject);
        Assert.Equal("198.51.100.7", entry.IpAddress);
        Assert.True(entry.Succeeded);
    }

    /// <summary>The follow-up message is published after the row is saved, and names the site.</summary>
    [Fact]
    public async Task The_follow_up_message_names_the_site_and_is_published_after_the_row_is_saved()
    {
        var account = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        var siteId = await SeedAsync(database, account);
        await using var context = SitesTestContext.Create(FakeCurrentUser.Customer(account), database);
        var bus = new RecordingMessageBus(() =>
        {
            // Read through a SECOND context: the tracker of the handler's own context would answer 5
            // whether or not SaveChanges had run.
            using var reader = SitesTestContext.Create(FakeCurrentUser.Admin(), database);
            return reader.Sites.Single().LogRetentionDays;
        });

        await Handler(context, account, bus).HandleAsync(
            new ChangeSiteLogRetentionCommand(5, siteId, "198.51.100.7", "tests"),
            CancellationToken.None);

        Assert.Equal(new SiteLogRetentionChanged(siteId), Assert.Single(bus.Published));
        Assert.Equal(5, bus.RetentionSeenAtPublish);
    }

    /// <summary>Another tenant's site is answered not found, nothing is published, and the probe is journalled.</summary>
    [Fact]
    public async Task Another_tenants_site_is_answered_not_found_nothing_is_published_and_the_probe_is_journalled()
    {
        var mine = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        var theirSiteId = await SeedAsync(database, Guid.NewGuid());
        var audit = new RecordingAuditWriter();
        var bus = new RecordingMessageBus();
        await using var context = SitesTestContext.Create(FakeCurrentUser.Customer(mine), database);

        var result = await Handler(context, mine, bus, audit).HandleAsync(
            new ChangeSiteLogRetentionCommand(5, theirSiteId, "198.51.100.7", "tests"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("SiteNotFound", result.Error!.Code);
        Assert.Empty(bus.Published);
        var entry = Assert.Single(audit.Entries);
        Assert.False(entry.Succeeded);
        Assert.Equal(theirSiteId.ToString(), entry.Subject);

        await using var reader = SitesTestContext.Create(FakeCurrentUser.Admin(), database);
        Assert.Equal(14, (await reader.Sites.SingleAsync()).LogRetentionDays);
    }

    /// <summary>Stores one site for an account.</summary>
    /// <param name="database">The in-memory database name.</param>
    /// <param name="accountId">The owning account.</param>
    /// <returns>The site's id.</returns>
    private static async Task<Guid> SeedAsync(string database, Guid accountId)
    {
        await using var context = SitesTestContext.Create(FakeCurrentUser.Admin(), database);
        var site = SitesTestContext.PhpSite(accountId, "example.com");
        context.Sites.Add(site);
        await context.SaveChangesAsync();

        return site.Id;
    }

    /// <summary>Builds the handler under test.</summary>
    /// <param name="context">The context bound to the calling principal.</param>
    /// <param name="accountId">The account the caller owns.</param>
    /// <param name="bus">The message bus double.</param>
    /// <param name="audit">The journal double.</param>
    /// <returns>The handler.</returns>
    private static ChangeSiteLogRetentionCommandHandler Handler(
        SitesDbContext context,
        Guid accountId,
        IMessageBus bus,
        RecordingAuditWriter? audit = null)
    {
        var accounts = new StubAccountDirectory(Snapshots.Account(accountId, "acme"));

        return new ChangeSiteLogRetentionCommandHandler(
            context,
            accounts,
            new SiteAuditJournal(audit ?? new RecordingAuditWriter(), FakeCurrentUser.Customer(accountId)),
            SitesTestContext.SiteNames(),
            bus);
    }
}
