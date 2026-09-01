using Maran.Modules.Sites.Commands.DeleteSite;
using Maran.Modules.Sites.Common;
using Maran.Modules.Sites.Domain;
using Maran.Modules.Sites.Domain.Enums;
using Maran.Modules.Sites.Persistence;
using Maran.Modules.Sites.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Sites.Tests.Commands.DeleteSite;

/// <summary>Behavioral contract of <see cref="DeleteSiteCommandHandler"/>.</summary>
public sealed class DeleteSiteCommandHandlerTests
{
    /// <summary>Deleting a site removes its vhost and then its row.</summary>
    [Fact]
    public async Task Deleting_a_site_removes_its_vhost_and_then_its_row()
    {
        var account = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        var siteId = await SeedAsync(database, account);
        var agent = new RecordingAgentSitesClient();
        await using var context = SitesTestContext.Create(FakeCurrentUser.Customer(account), database);

        var result = await Handler(context, account, agent).HandleAsync(
            new DeleteSiteCommand(siteId, "198.51.100.7", "tests"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        // The trailing 8.3 is the version whose php-fpm pool goes with the site, because this was
        // the account's only site on it. A pool naming a user that no longer resolves is what makes
        // the next php-fpm reload fail for every tenant on the host.
        Assert.Equal("delete:acme:example.com:8.3", Assert.Single(agent.Calls));
        Assert.Empty(await context.Sites.ToListAsync());
    }

    /// <summary>Deleting a site frees every hostname it claimed.</summary>
    [Fact]
    public async Task Deleting_a_site_frees_every_hostname_it_claimed()
    {
        // A claim that outlives its site is a name nobody on this server can ever use again — not
        // even the customer who just deleted it and wants it back.
        var account = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        Guid siteId;
        await using (var seed = SitesTestContext.Create(FakeCurrentUser.Admin(), database))
        {
            var site = SitesTestContext.PhpSite(account, "example.com", "8.3", "www.example.com");
            seed.Sites.Add(site);
            await seed.SaveChangesAsync();
            siteId = site.Id;
        }

        var agent = new RecordingAgentSitesClient();
        await using var context = SitesTestContext.Create(FakeCurrentUser.Customer(account), database);

        await Handler(context, account, agent).HandleAsync(
            new DeleteSiteCommand(siteId, "198.51.100.7", "tests"),
            CancellationToken.None);

        Assert.Empty(await context.SiteHostnames.IgnoreQueryFilters().ToListAsync());
    }

    /// <summary>A refused removal leaves the row in place.</summary>
    [Fact]
    public async Task A_refused_removal_leaves_the_row_in_place()
    {
        // The agent runs first so the surviving disagreement is the safe one: a vhost still serving
        // with its row intact, which a retry converges. The reverse would be a site nobody in the
        // panel can see and nobody can now take down.
        var account = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        var siteId = await SeedAsync(database, account);
        // The agent client's own code for a config the web server refused: AgentErrorTranslator is
        // the single wire-error boundary, so what a handler receives is one of ITS codes, never a
        // code this module invented (rules/csharp.md).
        var agent = new RecordingAgentSitesClient(Error.Of("AgentValidationFailed"));
        await using var context = SitesTestContext.Create(FakeCurrentUser.Customer(account), database);

        var result = await Handler(context, account, agent).HandleAsync(
            new DeleteSiteCommand(siteId, "198.51.100.7", "tests"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentValidationFailed", result.Error!.Code);
        Assert.Single(await context.Sites.ToListAsync());
    }

    /// <summary>Deleting another tenants site answers not found rather than forbidden.</summary>
    [Fact]
    public async Task Deleting_another_tenants_site_answers_not_found_rather_than_forbidden()
    {
        // The IDOR test, and the one where a 403 would be worst: it would confirm the site exists
        // to a caller who was trying to destroy it.
        var mine = Guid.NewGuid();
        var theirs = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        var theirSiteId = await SeedAsync(database, theirs);
        var agent = new RecordingAgentSitesClient();
        await using var context = SitesTestContext.Create(FakeCurrentUser.Customer(mine), database);

        var result = await Handler(context, mine, agent).HandleAsync(
            new DeleteSiteCommand(theirSiteId, "198.51.100.7", "tests"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("SiteNotFound", result.Error!.Code);
        Assert.Empty(agent.Calls);

        await using var owner = SitesTestContext.Create(FakeCurrentUser.Customer(theirs), database);
        Assert.Single(await owner.Sites.ToListAsync());
    }

    /// <summary>Deleting a site journals the domain of the site that is now gone.</summary>
    [Fact]
    public async Task Deleting_a_site_journals_the_domain_of_the_site_that_is_now_gone()
    {
        var account = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        var siteId = await SeedAsync(database, account);
        var audit = new RecordingAuditWriter();
        await using var context = SitesTestContext.Create(FakeCurrentUser.Customer(account), database);

        await Handler(context, account, new RecordingAgentSitesClient(), audit).HandleAsync(
            new DeleteSiteCommand(siteId, "198.51.100.7", "tests"),
            CancellationToken.None);

        var entry = Assert.Single(audit.Entries);
        Assert.Equal(AuditActions.SiteDeleted, entry.Action);
        Assert.Equal("example.com", entry.Subject);
    }

    /// <summary>Builds the handler under test.</summary>
    /// <param name="context">The context bound to the calling principal.</param>
    /// <param name="accountId">The account the caller owns.</param>
    /// <param name="agent">The agent double.</param>
    /// <param name="audit">The journal double.</param>
    private static DeleteSiteCommandHandler Handler(
        SitesDbContext context,
        Guid accountId,
        RecordingAgentSitesClient agent,
        RecordingAuditWriter? audit = null)
    {
        var accounts = new StubAccountDirectory(
            new AccountSnapshot(accountId, "acme", MaxSites: 5, MaxPhpWorkersPerPool: 10));
        return new DeleteSiteCommandHandler(
            context,
            accounts,
            agent,
            new SiteAuditJournal(audit ?? new RecordingAuditWriter(), FakeCurrentUser.Customer(accountId)));
    }

    /// <summary>Seeds one site and returns its id.</summary>
    /// <param name="database">The shared in-memory database.</param>
    /// <param name="accountId">The owning account.</param>
    private static async Task<Guid> SeedAsync(string database, Guid accountId)
    {
        await using var seed = SitesTestContext.Create(FakeCurrentUser.Admin(), database);
        var site = SitesTestContext.PhpSite(accountId, "example.com");
        seed.Sites.Add(site);
        await seed.SaveChangesAsync();
        return site.Id;
    }

    /// <summary>A pool another site of the account still uses is not retired with this one.</summary>
    /// <remarks>
    /// The case that has to be got right, because getting it wrong takes a site that nobody touched
    /// off the air. A php-fpm pool belongs to an ACCOUNT and a version: two of the account's sites
    /// on 8.3 share one pool and one worker budget, so deleting the first must leave it standing.
    /// </remarks>
    [Fact]
    public async Task A_pool_another_site_of_the_account_still_uses_is_not_retired_with_this_one()
    {
        var account = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        var siteId = await SeedAsync(database, account);
        await using (var second = SitesTestContext.Create(FakeCurrentUser.Admin(), database))
        {
            second.Sites.Add(SitesTestContext.PhpSite(account, "second.example.com"));
            await second.SaveChangesAsync();
        }

        var agent = new RecordingAgentSitesClient();
        await using var context = SitesTestContext.Create(FakeCurrentUser.Customer(account), database);

        var result = await Handler(context, account, agent).HandleAsync(
            new DeleteSiteCommand(siteId, "198.51.100.7", "tests"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        // The empty trailing field is the panel saying "leave every pool alone".
        Assert.Equal("delete:acme:example.com:", Assert.Single(agent.Calls));
    }

    /// <summary>Another accounts site on the same version does not keep this pool alive.</summary>
    /// <remarks>
    /// A pool is per account AND version, so a neighbour's 8.3 pool is a different file. Scoping
    /// the "is it still needed" question to the account is what stops a busy server, on which
    /// somebody always has a site on 8.3, from never retiring a pool at all.
    /// </remarks>
    [Fact]
    public async Task Another_accounts_site_on_the_same_version_does_not_keep_this_pool_alive()
    {
        var account = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        var siteId = await SeedAsync(database, account);
        await using (var neighbour = SitesTestContext.Create(FakeCurrentUser.Admin(), database))
        {
            neighbour.Sites.Add(SitesTestContext.PhpSite(Guid.NewGuid(), "neighbour.example.com"));
            await neighbour.SaveChangesAsync();
        }

        var agent = new RecordingAgentSitesClient();
        await using var context = SitesTestContext.Create(FakeCurrentUser.Customer(account), database);

        await Handler(context, account, agent).HandleAsync(
            new DeleteSiteCommand(siteId, "198.51.100.7", "tests"),
            CancellationToken.None);

        Assert.Equal("delete:acme:example.com:8.3", Assert.Single(agent.Calls));
    }

    /// <summary>A static site retires no pool because it never had one.</summary>
    [Fact]
    public async Task A_static_site_retires_no_pool_because_it_never_had_one()
    {
        var account = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        Guid siteId;
        await using (var seed = SitesTestContext.Create(FakeCurrentUser.Admin(), database))
        {
            var site = new Site(
                Guid.NewGuid(),
                account,
                "static.example.com",
                [],
                SiteBackendType.Static,
                string.Empty,
                string.Empty,
                "/home/acme/sites/static.example.com",
                new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
            seed.Sites.Add(site);
            await seed.SaveChangesAsync();
            siteId = site.Id;
        }

        var agent = new RecordingAgentSitesClient();
        await using var context = SitesTestContext.Create(FakeCurrentUser.Customer(account), database);

        await Handler(context, account, agent).HandleAsync(
            new DeleteSiteCommand(siteId, "198.51.100.7", "tests"),
            CancellationToken.None);

        Assert.Equal("delete:acme:static.example.com:", Assert.Single(agent.Calls));
    }
}
