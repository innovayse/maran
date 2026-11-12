using Maran.Modules.Sites.Domain.Entities;
using Maran.Modules.Sites.IntegrationEvents.Handlers;
using Maran.Modules.Sites.Services;
using Maran.Modules.Sites.Tests.TestSupport;
using Maran.Sdk.Events;
using Maran.Sdk.Modules;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Sites.Tests.IntegrationEvents.Handlers;

/// <summary>
/// How the account cascades (suspend, resume, delete) behave around the per-site lock (#561): they render from the
/// site as it is once they hold it, they wait a bounded time in all, and a pass that could not finish is finished by
/// repeating it.
/// </summary>
public sealed class AccountCascadeLockTests
{
    /// <summary>The system user name the events carry.</summary>
    private const string Username = "acme";

    /// <summary>Resume renders the options a customer saved while the resume waited for the site.</summary>
    /// <remarks>
    /// The defect: the row was reloaded in place, which refreshes the site's own columns and leaves the
    /// separate options entities as the listing loaded them, so the vhost was written without the new rules.
    /// </remarks>
    [Fact]
    public async Task A_resume_renders_the_options_saved_while_it_waited()
    {
        var (account, database, site) = await SeedOneAsync();
        var locks = new ScriptedSiteLock(new SiteOperationLock());
        locks.AfterGrant = id =>
        {
            return SaveOptionsOnSiteAsync(database, site.Id, id);
        };
        var agent = new RecordingAgentSitesClient();
        await using var context = SitesTestContext.Create(FakeCurrentUser.Admin(), database);

        await new AccountResumingHandler(context, agent, SiteStacks.Ready(), Limits(account), locks, new FakeClock(DateTimeOffset.UnixEpoch))
            .HandleAsync(new AccountResuming(account, Username), CancellationToken.None);

        Assert.Single(Assert.Single(agent.Descriptors).Options.BasicAuthUsers);
    }

    /// <summary>The accepting control: with nothing saved meanwhile, the resume renders the options the site has.</summary>
    [Fact]
    public async Task A_resume_with_nothing_saved_meanwhile_renders_the_stored_options()
    {
        var (account, database, _) = await SeedOneAsync();
        var agent = new RecordingAgentSitesClient();
        await using var context = SitesTestContext.Create(FakeCurrentUser.Admin(), database);

        await new AccountResumingHandler(
                context, agent, SiteStacks.Ready(), Limits(account), new SiteOperationLock(), new FakeClock(DateTimeOffset.UnixEpoch))
            .HandleAsync(new AccountResuming(account, Username), CancellationToken.None);

        Assert.Empty(Assert.Single(agent.Descriptors).Options.BasicAuthUsers);
    }

    /// <summary>Suspend renders the stub from the options saved while it waited, so the agent recognises the stub.</summary>
    [Fact]
    public async Task A_suspension_renders_the_stub_from_the_options_saved_while_it_waited()
    {
        var (account, database, site) = await SeedOneAsync();
        var locks = new ScriptedSiteLock(new SiteOperationLock());
        locks.AfterGrant = id =>
        {
            return SaveOptionsOnSiteAsync(database, site.Id, id);
        };
        var agent = new RecordingAgentSitesClient();
        await using var context = SitesTestContext.Create(FakeCurrentUser.Admin(), database);

        await new AccountSuspendingHandler(context, agent, SiteStacks.Ready(), Limits(account), locks, new FakeClock(DateTimeOffset.UnixEpoch))
            .HandleAsync(new AccountSuspending(account, Username), CancellationToken.None);

        Assert.Single(Assert.Single(agent.Descriptors).Options.BasicAuthUsers);
    }

    /// <summary>An account delete retires the version a site holds now, not the one the listing saw.</summary>
    [Fact]
    public async Task An_account_delete_retires_the_version_a_site_holds_now()
    {
        var account = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        var first = SitesTestContext.PhpSite(account, "a.example", "8.3");
        var second = SitesTestContext.PhpSite(account, "b.example", "8.3");
        await SeedAsync(database, first, second);
        var locks = new ScriptedSiteLock(new SiteOperationLock());
        locks.AfterGrant = async id =>
        {
            // Granted for the first site, so b has already moved when the cascade reaches it.
            if (id == first.Id)
            {
                await using var other = SitesTestContext.Create(FakeCurrentUser.Admin(), database);
                (await other.Sites.SingleAsync(site => site.Id == second.Id)).ChangePhpVersion("8.4");
                await other.SaveChangesAsync();
            }
        };
        var agent = new RecordingAgentSitesClient();
        await using var context = SitesTestContext.Create(FakeCurrentUser.Admin(), database);

        await new AccountDeletingHandler(context, agent, SiteStacks.Ready(), locks, new FakeClock(DateTimeOffset.UnixEpoch))
            .HandleAsync(new AccountDeleting(account, Username), CancellationToken.None);

        // b moved to 8.4 while the cascade waited, so a is the last site on 8.3 and b the only one on 8.4; the
        // listing, which saw both on 8.3, would have left 8.3 without a retirement.
        Assert.Equal(["delete:acme:a.example:8.3", "delete:acme:b.example:8.4"], agent.Calls);
    }

    /// <summary>The accepting control: two sites that both stay on a version retire it once, on the last.</summary>
    [Fact]
    public async Task An_account_delete_retires_a_shared_version_once_on_the_last_site()
    {
        var account = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        await SeedAsync(database, SitesTestContext.PhpSite(account, "a.example", "8.3"), SitesTestContext.PhpSite(account, "b.example", "8.3"));
        var agent = new RecordingAgentSitesClient();
        await using var context = SitesTestContext.Create(FakeCurrentUser.Admin(), database);

        await new AccountDeletingHandler(context, agent, SiteStacks.Ready(), new SiteOperationLock(), new FakeClock(DateTimeOffset.UnixEpoch))
            .HandleAsync(new AccountDeleting(account, Username), CancellationToken.None);

        Assert.Equal(["delete:acme:a.example:", "delete:acme:b.example:8.3"], agent.Calls);
    }

    /// <summary>A suspension that could not stub one busy site stubs the others, fails, and a repeat finishes it.</summary>
    [Fact]
    public async Task A_suspension_with_one_busy_site_stubs_the_others_and_a_repeat_finishes_it()
    {
        var account = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        var sites = new[]
        {
            SitesTestContext.PhpSite(account, "a.example"),
            SitesTestContext.PhpSite(account, "b.example"),
            SitesTestContext.PhpSite(account, "c.example"),
        };
        await SeedAsync(database, sites);
        var locks = new ScriptedSiteLock(new SiteOperationLock());
        locks.Busy.Add(sites[1].Id);
        var agent = new RecordingAgentSitesClient();
        await using var context = SitesTestContext.Create(FakeCurrentUser.Admin(), database);
        var handler = new AccountSuspendingHandler(
            context, agent, SiteStacks.Ready(), Limits(account), locks, new FakeClock(DateTimeOffset.UnixEpoch));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
        {
            return handler.HandleAsync(new AccountSuspending(account, Username), CancellationToken.None);
        });
        Assert.Equal(["disable:acme:a.example", "disable:acme:c.example"], agent.Calls);

        locks.Busy.Clear();
        await handler.HandleAsync(new AccountSuspending(account, Username), CancellationToken.None);

        Assert.Contains("disable:acme:b.example", agent.Calls);
    }

    /// <summary>A resumption with one busy site restores the others, fails, and a repeat restores the rest.</summary>
    [Fact]
    public async Task A_resumption_with_one_busy_site_restores_the_others_and_a_repeat_finishes_it()
    {
        var account = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        var sites = new[] { SitesTestContext.PhpSite(account, "a.example"), SitesTestContext.PhpSite(account, "b.example") };
        await SeedAsync(database, sites);
        var locks = new ScriptedSiteLock(new SiteOperationLock());
        locks.Busy.Add(sites[0].Id);
        var agent = new RecordingAgentSitesClient();
        await using var context = SitesTestContext.Create(FakeCurrentUser.Admin(), database);
        var handler = new AccountResumingHandler(
            context, agent, SiteStacks.Ready(), Limits(account), locks, new FakeClock(DateTimeOffset.UnixEpoch));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
        {
            return handler.HandleAsync(new AccountResuming(account, Username), CancellationToken.None);
        });
        Assert.Equal(["enable:acme:b.example"], agent.Calls);

        locks.Busy.Clear();
        await handler.HandleAsync(new AccountResuming(account, Username), CancellationToken.None);

        Assert.Contains("enable:acme:a.example", agent.Calls);
    }

    /// <summary>An account delete with one busy site removes no row, and a repeat removes the vhosts left and then every row.</summary>
    [Fact]
    public async Task An_account_delete_with_one_busy_site_keeps_every_row_and_a_repeat_finishes_it()
    {
        var account = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        var sites = new[] { SitesTestContext.PhpSite(account, "a.example"), SitesTestContext.PhpSite(account, "b.example") };
        await SeedAsync(database, sites);
        var locks = new ScriptedSiteLock(new SiteOperationLock());
        locks.Busy.Add(sites[1].Id);
        var agent = new RecordingAgentSitesClient();
        await using var context = SitesTestContext.Create(FakeCurrentUser.Admin(), database);
        var handler = new AccountDeletingHandler(context, agent, SiteStacks.Ready(), locks, new FakeClock(DateTimeOffset.UnixEpoch));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
        {
            return handler.HandleAsync(new AccountDeleting(account, Username), CancellationToken.None);
        });
        await using (var reader = SitesTestContext.Create(FakeCurrentUser.Admin(), database))
        {
            Assert.Equal(2, await reader.Sites.CountAsync());
        }

        locks.Busy.Clear();
        await handler.HandleAsync(new AccountDeleting(account, Username), CancellationToken.None);

        Assert.Contains("delete:acme:b.example:8.3", agent.Calls);
        await using var after = SitesTestContext.Create(FakeCurrentUser.Admin(), database);
        Assert.Empty(await after.Sites.ToListAsync());
    }

    /// <summary>However many sites are busy, the cascade waits at most its budget in all, and a free site is still done.</summary>
    [Fact]
    public async Task A_cascade_waits_at_most_its_budget_in_all_and_still_does_the_free_site()
    {
        var account = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        var sites = new[]
        {
            SitesTestContext.PhpSite(account, "a.example"),
            SitesTestContext.PhpSite(account, "b.example"),
            SitesTestContext.PhpSite(account, "c.example"),
            SitesTestContext.PhpSite(account, "d.example"),
            SitesTestContext.PhpSite(account, "e.example"),
        };
        await SeedAsync(database, sites);
        var clock = new MovableClock();
        var locks = new ScriptedSiteLock(new SiteOperationLock(), clock);
        locks.Busy.UnionWith(sites.Take(4).Select(site =>
        {
            return site.Id;
        }));
        var agent = new RecordingAgentSitesClient();
        await using var context = SitesTestContext.Create(FakeCurrentUser.Admin(), database);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
        {
            return new AccountSuspendingHandler(context, agent, SiteStacks.Ready(), Limits(account), locks, clock)
                .HandleAsync(new AccountSuspending(account, Username), CancellationToken.None);
        });

        // Three busy sites spend the whole budget (3 x 15 s of 45 s); the fourth is asked without waiting,
        // the free fifth still gets its stub.
        Assert.Equal(
            [SiteLockWait.Cascade, SiteLockWait.Cascade, SiteLockWait.Cascade, TimeSpan.Zero, TimeSpan.Zero],
            locks.Waits);
        Assert.Equal(SiteLockWait.CascadeTotal, locks.Waits.Aggregate(TimeSpan.Zero, (sum, wait) => { return sum + wait; }));
        Assert.Equal(["disable:acme:e.example"], agent.Calls);
    }

    /// <summary>Saves a customer's options on the site, once its lock is granted, as a request that committed during the wait would.</summary>
    /// <param name="database">The shared in-memory database.</param>
    /// <param name="siteId">The site the options are saved on.</param>
    /// <param name="grantedId">The key the lock was just granted for.</param>
    private static async Task SaveOptionsOnSiteAsync(string database, Guid siteId, Guid grantedId)
    {
        if (grantedId != siteId)
        {
            return;
        }

        await using var other = SitesTestContext.Create(FakeCurrentUser.Admin(), database);
        (await other.Sites.SingleAsync(site => site.Id == siteId)).ReplaceOptions(SiteOptionsSamples.Full());
        await other.SaveChangesAsync();
    }

    /// <summary>Seeds one enabled PHP site that has no options.</summary>
    /// <returns>The account, the database and the site.</returns>
    private static async Task<(Guid Account, string Database, Site Site)> SeedOneAsync()
    {
        var account = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        var site = SitesTestContext.PhpSite(account, "a.example");
        await SeedAsync(database, site);

        return (account, database, site);
    }

    /// <summary>Reads the plan bounds of an account.</summary>
    /// <param name="account">The account.</param>
    /// <returns>The reader over a directory holding it.</returns>
    private static SitePlanLimits Limits(Guid account)
    {
        return new SitePlanLimits(new StubAccountDirectory(Snapshots.Account(account, Username)));
    }

    /// <summary>Seeds the given sites into a shared in-memory database.</summary>
    /// <param name="database">The in-memory database name.</param>
    /// <param name="sites">The rows to write.</param>
    private static async Task SeedAsync(string database, params Site[] sites)
    {
        await using var context = SitesTestContext.Create(FakeCurrentUser.Admin(), database);
        context.Sites.AddRange(sites);
        await context.SaveChangesAsync();
    }
}
