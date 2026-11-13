using Maran.Agent.Client.Interfaces;
using Maran.Modules.Sites.Commands.ChangeSitePhpVersion;
using Maran.Modules.Sites.Commands.DeleteSite;
using Maran.Modules.Sites.Commands.DisableSite;
using Maran.Modules.Sites.Commands.EnableSite;
using Maran.Modules.Sites.Commands.ReplaceSiteOptions;
using Maran.Modules.Sites.Domain.Enums;
using Maran.Modules.Sites.IntegrationEvents.Events;
using Maran.Modules.Sites.IntegrationEvents.Handlers;
using Maran.Modules.Sites.Persistence;
using Maran.Modules.Sites.Services;
using Maran.Modules.Sites.Tests.TestSupport;
using Maran.Sdk.Events;
using Maran.Sdk.Interfaces;
using Maran.Sdk.Modules;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Sites.Tests.Concurrency;

/// <summary>
/// Two operations on one site never interleave (#561). Each race parks the first operation inside the agent
/// call and starts the second on purpose: the second must wait for the first and then act on what the first
/// committed. Without the per-site lock the second would run beside the first and act on the row it read before.
/// </summary>
public sealed class SiteOperationBarrierTests
{
    /// <summary>The system user name of the account.</summary>
    private const string Username = "acme";

    /// <summary>The longest any waiting step may take before the test calls it a hang.</summary>
    private static readonly TimeSpan Hang = TimeSpan.FromSeconds(10);

    /// <summary>A re-render that starts while a disable is in the agent waits, then finds the site disabled and renders nothing.</summary>
    [Fact]
    public async Task A_rerender_that_starts_during_a_disable_waits_and_then_skips_the_disabled_site()
    {
        var account = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        var siteId = await SeedAsync(database, account, "a.example", enabled: true);
        var locks = new ParkingSiteLock(new SiteOperationLock());
        var barrier = new BarrierAgentSitesClient(new RecordingAgentSitesClient(), "disable");
        await using var disableContext = SitesTestContext.Create(FakeCurrentUser.Customer(account), database);
        await using var rerenderContext = SitesTestContext.Create(FakeCurrentUser.Admin(), database);

        var disable = DisableHandler(disableContext, account, barrier, locks).HandleAsync(
            new DisableSiteCommand(siteId, "198.51.100.7", "tests"), CancellationToken.None);
        await barrier.Entered;
        var rerender = RerenderHandler(rerenderContext, barrier, account, locks).HandleAsync(
            new PhpSiteVhostsRerenderRequested(account), CancellationToken.None);

        // Parked inside the lock is an event, not a pause: a lock that let the second operation through never
        // parks it, and this wait then times out instead of the test passing on a slow machine.
        await locks.Parked.WaitAsync(Hang);
        Assert.False(rerender.IsCompleted);
        Assert.DoesNotContain(barrier.Inner.Calls, call => { return call.StartsWith("change-php:", StringComparison.Ordinal); });

        barrier.Release();
        await disable;
        await rerender;

        Assert.Equal(["disable:acme:a.example"], barrier.Inner.Calls);
    }

    /// <summary>Inverse control: with the site free, the same re-render renders the enabled site.</summary>
    [Fact]
    public async Task A_rerender_of_a_free_enabled_site_renders_it()
    {
        var account = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        await SeedAsync(database, account, "a.example", enabled: true);
        var agent = new RecordingAgentSitesClient();
        await using var context = SitesTestContext.Create(FakeCurrentUser.Admin(), database);

        await RerenderHandler(context, agent, account, new SiteOperationLock()).HandleAsync(
            new PhpSiteVhostsRerenderRequested(account), CancellationToken.None);

        Assert.Equal(["change-php:acme:a.example:8.3:False"], agent.Calls);
    }

    /// <summary>An enable and a disable of one site run one after the other and the later one wins.</summary>
    [Fact]
    public async Task An_enable_and_a_disable_of_one_site_run_one_after_the_other()
    {
        var account = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        var siteId = await SeedAsync(database, account, "a.example", enabled: false);
        var locks = new ParkingSiteLock(new SiteOperationLock());
        var barrier = new BarrierAgentSitesClient(new RecordingAgentSitesClient(), "enable");
        await using var enableContext = SitesTestContext.Create(FakeCurrentUser.Customer(account), database);
        await using var disableContext = SitesTestContext.Create(FakeCurrentUser.Customer(account), database);

        var enable = EnableHandler(enableContext, account, barrier, locks).HandleAsync(
            new EnableSiteCommand(siteId, "198.51.100.7", "tests"), CancellationToken.None);
        await barrier.Entered;
        var disable = DisableHandler(disableContext, account, barrier, locks).HandleAsync(
            new DisableSiteCommand(siteId, "198.51.100.7", "tests"), CancellationToken.None);

        // Parked inside the lock is an event, not a pause: a lock that let the second operation through never
        // parks it, and this wait then times out instead of the test passing on a slow machine.
        await locks.Parked.WaitAsync(Hang);
        Assert.False(disable.IsCompleted);
        Assert.Equal(["enable:acme:a.example"], barrier.Inner.Calls);

        barrier.Release();
        Assert.True((await enable).IsSuccess);
        Assert.True((await disable).IsSuccess);

        Assert.Equal(["enable:acme:a.example", "disable:acme:a.example"], barrier.Inner.Calls);
        await using var reader = SitesTestContext.Create(FakeCurrentUser.Admin(), database);
        Assert.Equal(SiteStatus.Disabled, (await reader.Sites.SingleAsync()).Status);
    }

    /// <summary>A re-render that starts during a delete finds the site gone and renders nothing.</summary>
    [Fact]
    public async Task A_rerender_that_starts_during_a_delete_finds_the_site_gone()
    {
        var account = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        var siteId = await SeedAsync(database, account, "a.example", enabled: true);
        var locks = new ParkingSiteLock(new SiteOperationLock());
        var barrier = new BarrierAgentSitesClient(new RecordingAgentSitesClient(), "delete");
        await using var deleteContext = SitesTestContext.Create(FakeCurrentUser.Customer(account), database);
        await using var rerenderContext = SitesTestContext.Create(FakeCurrentUser.Admin(), database);

        var delete = DeleteHandler(deleteContext, account, barrier, locks).HandleAsync(
            new DeleteSiteCommand(siteId, "198.51.100.7", "tests"), CancellationToken.None);
        await barrier.Entered;
        var rerender = RerenderHandler(rerenderContext, barrier, account, locks).HandleAsync(
            new PhpSiteVhostsRerenderRequested(account), CancellationToken.None);

        // Parked inside the lock is an event, not a pause: a lock that let the second operation through never
        // parks it, and this wait then times out instead of the test passing on a slow machine.
        await locks.Parked.WaitAsync(Hang);
        Assert.False(rerender.IsCompleted);

        barrier.Release();
        Assert.True((await delete).IsSuccess);
        await rerender;

        Assert.Equal(["delete:acme:a.example:8.3"], barrier.Inner.Calls);
    }

    /// <summary>Saving options during a PHP version change waits and then renders the vhost with the new version.</summary>
    [Fact]
    public async Task Saving_options_during_a_php_version_change_renders_the_new_version()
    {
        var account = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        var siteId = await SeedAsync(database, account, "a.example", enabled: true);
        var locks = new ParkingSiteLock(new SiteOperationLock());
        var barrier = new BarrierAgentSitesClient(new RecordingAgentSitesClient(), "change-php");
        await using var versionContext = SitesTestContext.Create(FakeCurrentUser.Customer(account), database);
        await using var optionsContext = SitesTestContext.Create(FakeCurrentUser.Customer(account), database);

        var version = ChangeVersionHandler(versionContext, account, barrier, locks).HandleAsync(
            new ChangeSitePhpVersionCommand(siteId, "8.4", "198.51.100.7", "tests"), CancellationToken.None);
        await barrier.Entered;
        var options = OptionsHandler(optionsContext, account, barrier, locks).HandleAsync(
            new ReplaceSiteOptionsCommand(siteId, CanonicalHost.None, null, null, [], [], null, [], null, "198.51.100.7", "tests"),
            CancellationToken.None);

        // Parked inside the lock is an event, not a pause: a lock that let the second operation through never
        // parks it, and this wait then times out instead of the test passing on a slow machine.
        await locks.Parked.WaitAsync(Hang);
        Assert.False(options.IsCompleted);
        Assert.DoesNotContain(barrier.Inner.Calls, call => { return call.StartsWith("enable:", StringComparison.Ordinal); });

        barrier.Release();
        Assert.True((await version).IsSuccess);
        Assert.True((await options).IsSuccess);

        Assert.Equal("8.4", barrier.Inner.Descriptors[^1].PhpVersion);
    }

    /// <summary>An operation on another site does not wait for a parked one.</summary>
    [Fact]
    public async Task An_operation_on_another_site_does_not_wait_for_a_parked_one()
    {
        var account = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        var first = await SeedAsync(database, account, "a.example", enabled: true);
        var second = await SeedAsync(database, account, "b.example", enabled: true);
        var locks = new ParkingSiteLock(new SiteOperationLock());
        var barrier = new BarrierAgentSitesClient(new RecordingAgentSitesClient(), "disable");
        await using var firstContext = SitesTestContext.Create(FakeCurrentUser.Customer(account), database);
        await using var secondContext = SitesTestContext.Create(FakeCurrentUser.Customer(account), database);

        var parked = DisableHandler(firstContext, account, barrier, locks).HandleAsync(
            new DisableSiteCommand(first, "198.51.100.7", "tests"), CancellationToken.None);
        await barrier.Entered;
        var other = await EnableHandler(secondContext, account, barrier, locks).HandleAsync(
            new EnableSiteCommand(second, "198.51.100.7", "tests"), CancellationToken.None);

        Assert.True(other.IsSuccess);
        Assert.False(parked.IsCompleted);

        barrier.Release();
        await parked;
    }

    /// <summary>A site that stays busy refuses the command with SiteBusy, journals it and never reaches the agent.</summary>
    [Fact]
    public async Task A_busy_site_refuses_the_command_with_site_busy_and_never_reaches_the_agent()
    {
        var account = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        var siteId = await SeedAsync(database, account, "a.example", enabled: true);
        var agent = new RecordingAgentSitesClient();
        var audit = new RecordingAuditWriter();
        await using var context = SitesTestContext.Create(FakeCurrentUser.Customer(account), database);

        var result = await DisableHandler(context, account, agent, new BusySiteLock(), audit).HandleAsync(
            new DisableSiteCommand(siteId, "198.51.100.7", "tests"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("SiteBusy", result.Error!.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Empty(agent.Calls);
        Assert.Single(audit.Entries);
    }

    /// <summary>The other four commands answer SiteBusy the same way.</summary>
    [Fact]
    public async Task Every_site_command_answers_site_busy_when_the_site_stays_busy()
    {
        var account = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        var siteId = await SeedAsync(database, account, "a.example", enabled: true);
        var agent = new RecordingAgentSitesClient();
        var busy = new BusySiteLock();
        await using var context = SitesTestContext.Create(FakeCurrentUser.Customer(account), database);

        var enable = await EnableHandler(context, account, agent, busy).HandleAsync(
            new EnableSiteCommand(siteId, "198.51.100.7", "tests"), CancellationToken.None);
        var delete = await DeleteHandler(context, account, agent, busy).HandleAsync(
            new DeleteSiteCommand(siteId, "198.51.100.7", "tests"), CancellationToken.None);
        var version = await ChangeVersionHandler(context, account, agent, busy).HandleAsync(
            new ChangeSitePhpVersionCommand(siteId, "8.4", "198.51.100.7", "tests"), CancellationToken.None);
        var options = await OptionsHandler(context, account, agent, busy).HandleAsync(
            new ReplaceSiteOptionsCommand(siteId, CanonicalHost.None, null, null, [], [], null, [], null, "198.51.100.7", "tests"),
            CancellationToken.None);

        Assert.Equal("SiteBusy", enable.Error!.Code);
        Assert.Equal("SiteBusy", delete.Error!.Code);
        Assert.Equal("SiteBusy", version.Error!.Code);
        Assert.Equal("SiteBusy", options.Error!.Code);
        Assert.Empty(agent.Calls);
        Assert.Equal(4, busy.Attempts);
    }

    /// <summary>A re-render of a busy site renders nothing and fails the message so it is retried.</summary>
    [Fact]
    public async Task A_rerender_of_a_busy_site_fails_the_message_so_it_is_retried()
    {
        var account = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        await SeedAsync(database, account, "a.example", enabled: true);
        var agent = new RecordingAgentSitesClient();
        await using var context = SitesTestContext.Create(FakeCurrentUser.Admin(), database);

        await Assert.ThrowsAsync<SiteBusyException>(async () =>
        {
            await RerenderHandler(context, agent, account, new BusySiteLock()).HandleAsync(
                new PhpSiteVhostsRerenderRequested(account), CancellationToken.None);
        });
        Assert.Empty(agent.Calls);
    }

    /// <summary>Suspending, resuming and deleting an account with a busy site fail after trying every site, with no agent call.</summary>
    [Fact]
    public async Task The_account_lifecycle_jobs_fail_when_a_site_stays_busy()
    {
        var account = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        await SeedAsync(database, account, "a.example", enabled: true);
        var agent = new RecordingAgentSitesClient();
        var busy = new BusySiteLock();
        await using var context = SitesTestContext.Create(FakeCurrentUser.Admin(), database);
        var limits = new SitePlanLimits(new StubAccountDirectory(Snapshots.Account(account, Username)));

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await new AccountSuspendingHandler(context, agent, SiteStacks.Ready(), limits, busy, new FakeClock(DateTimeOffset.UnixEpoch)).HandleAsync(
                new AccountSuspending(account, Username), CancellationToken.None);
        });
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await new AccountResumingHandler(context, agent, SiteStacks.Ready(), limits, busy, new FakeClock(DateTimeOffset.UnixEpoch)).HandleAsync(
                new AccountResuming(account, Username), CancellationToken.None);
        });
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await new AccountDeletingHandler(context, agent, SiteStacks.Ready(), busy, new FakeClock(DateTimeOffset.UnixEpoch)).HandleAsync(
                new AccountDeleting(account, Username), CancellationToken.None);
        });

        Assert.Empty(agent.Calls);
        Assert.Equal(3, busy.Attempts);
        Assert.Single(await context.Sites.ToListAsync());
    }

    /// <summary>A resume that starts during a disable waits and leaves the customer-disabled site a stub.</summary>
    [Fact]
    public async Task A_resume_that_starts_during_a_disable_leaves_the_disabled_site_a_stub()
    {
        var account = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        var siteId = await SeedAsync(database, account, "a.example", enabled: true);
        var locks = new ParkingSiteLock(new SiteOperationLock());
        var barrier = new BarrierAgentSitesClient(new RecordingAgentSitesClient(), "disable");
        await using var disableContext = SitesTestContext.Create(FakeCurrentUser.Customer(account), database);
        await using var resumeContext = SitesTestContext.Create(FakeCurrentUser.Admin(), database);

        var disable = DisableHandler(disableContext, account, barrier, locks).HandleAsync(
            new DisableSiteCommand(siteId, "198.51.100.7", "tests"), CancellationToken.None);
        await barrier.Entered;
        var resume = new AccountResumingHandler(
            resumeContext,
            barrier,
            SiteStacks.Ready(),
            new SitePlanLimits(new StubAccountDirectory(Snapshots.Account(account, Username))),
            locks,
            new FakeClock(DateTimeOffset.UnixEpoch)).HandleAsync(new AccountResuming(account, Username), CancellationToken.None);

        // Parked inside the lock is an event, not a pause: a lock that let the second operation through never
        // parks it, and this wait then times out instead of the test passing on a slow machine.
        await locks.Parked.WaitAsync(Hang);
        Assert.False(resume.IsCompleted);

        barrier.Release();
        await disable;
        await resume;

        Assert.Equal(["disable:acme:a.example"], barrier.Inner.Calls);
    }

    /// <summary>
    /// A delete of the last site on a version waits for a version change that is moving ANOTHER site onto that
    /// version, so it sees the move and leaves the pool the other site has just been given.
    /// </summary>
    /// <remarks>
    /// The two sites have different locks, so only the account's pool lock can order them. Without it the delete
    /// reads the other site still on its old version, retires the pool and takes the moved site off the air.
    /// </remarks>
    [Fact]
    public async Task A_delete_of_the_last_site_on_a_version_waits_for_a_move_onto_that_version()
    {
        var account = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        var leaving = await SeedAsync(database, account, "a.example", enabled: true, "8.4");
        var moving = await SeedAsync(database, account, "b.example", enabled: true, "8.3");
        var locks = new ParkingSiteLock(new SiteOperationLock());
        var barrier = new BarrierAgentSitesClient(new RecordingAgentSitesClient(), "change-php");
        await using var moveContext = SitesTestContext.Create(FakeCurrentUser.Customer(account), database);
        await using var deleteContext = SitesTestContext.Create(FakeCurrentUser.Customer(account), database);

        var move = ChangeVersionHandler(moveContext, account, barrier, locks).HandleAsync(
            new ChangeSitePhpVersionCommand(moving, "8.4", "198.51.100.7", "tests"), CancellationToken.None);
        await barrier.Entered;
        var delete = DeleteHandler(deleteContext, account, barrier, locks).HandleAsync(
            new DeleteSiteCommand(leaving, "198.51.100.7", "tests"), CancellationToken.None);

        // Parked on the account's pools: the sites themselves are different, so nothing else could hold it.
        await locks.Parked.WaitAsync(Hang);
        Assert.False(delete.IsCompleted);
        Assert.DoesNotContain(barrier.Inner.Calls, call => { return call.StartsWith("delete:", StringComparison.Ordinal); });

        barrier.Release();
        Assert.True((await move).IsSuccess);
        Assert.True((await delete).IsSuccess);

        // Nothing is retired: b now runs on 8.4, so 8.4's pool is still needed.
        Assert.Equal(["change-php:acme:b.example:8.4:True", "delete:acme:a.example:"], barrier.Inner.Calls);
    }

    /// <summary>The accepting control: with no move in flight, the delete of the last site on a version retires its pool.</summary>
    [Fact]
    public async Task A_delete_of_the_last_site_on_a_version_retires_it_when_nothing_moves()
    {
        var account = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        var leaving = await SeedAsync(database, account, "a.example", enabled: true, "8.4");
        await SeedAsync(database, account, "b.example", enabled: true, "8.3");
        var agent = new RecordingAgentSitesClient();
        await using var context = SitesTestContext.Create(FakeCurrentUser.Customer(account), database);

        var result = await DeleteHandler(context, account, agent, new SiteOperationLock()).HandleAsync(
            new DeleteSiteCommand(leaving, "198.51.100.7", "tests"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(["delete:acme:a.example:8.4"], agent.Calls);
    }

    /// <summary>Seeds one PHP site.</summary>
    /// <param name="database">The shared in-memory database.</param>
    /// <param name="accountId">The owning account.</param>
    /// <param name="domain">The site's domain.</param>
    /// <param name="enabled">Whether the site starts enabled.</param>
    /// <param name="phpVersion">The PHP version the site runs.</param>
    private static async Task<Guid> SeedAsync(string database, Guid accountId, string domain, bool enabled, string phpVersion = "8.3")
    {
        await using var seed = SitesTestContext.Create(FakeCurrentUser.Admin(), database);
        var site = SitesTestContext.PhpSite(accountId, domain, phpVersion);
        if (enabled)
        {
            site.Enable();
        }
        else
        {
            site.Disable();
        }

        seed.Sites.Add(site);
        await seed.SaveChangesAsync();

        return site.Id;
    }

    /// <summary>The stub directory holding the account.</summary>
    /// <param name="accountId">The account.</param>
    private static StubAccountDirectory Accounts(Guid accountId)
    {
        return new StubAccountDirectory(Snapshots.Account(accountId, Username));
    }

    /// <summary>Builds the journal over a recording writer.</summary>
    /// <param name="accountId">The calling account.</param>
    /// <param name="audit">The writer, or a new one.</param>
    private static SiteAuditJournal Journal(Guid accountId, RecordingAuditWriter? audit)
    {
        return new SiteAuditJournal(audit ?? new RecordingAuditWriter(), FakeCurrentUser.Customer(accountId));
    }

    /// <summary>Builds the disable handler.</summary>
    private static DisableSiteCommandHandler DisableHandler(
        SitesDbContext context, Guid account, IAgentSitesClient agent, ISiteOperationLock locks, RecordingAuditWriter? audit = null)
    {
        return new DisableSiteCommandHandler(
            context, Accounts(account), agent, Journal(account, audit), SitesTestContext.SiteNames(), SiteStacks.Ready(), locks);
    }

    /// <summary>Builds the enable handler.</summary>
    private static EnableSiteCommandHandler EnableHandler(
        SitesDbContext context, Guid account, IAgentSitesClient agent, ISiteOperationLock locks)
    {
        return new EnableSiteCommandHandler(
            context, Accounts(account), agent, Journal(account, null), SitesTestContext.SiteNames(), SiteStacks.Ready(), locks);
    }

    /// <summary>Builds the delete handler.</summary>
    private static DeleteSiteCommandHandler DeleteHandler(
        SitesDbContext context, Guid account, IAgentSitesClient agent, ISiteOperationLock locks)
    {
        return new DeleteSiteCommandHandler(
            context, Accounts(account), agent, Journal(account, null), SiteStacks.Ready(), locks);
    }

    /// <summary>Builds the PHP version handler.</summary>
    private static ChangeSitePhpVersionCommandHandler ChangeVersionHandler(
        SitesDbContext context, Guid account, IAgentSitesClient agent, ISiteOperationLock locks)
    {
        return new ChangeSitePhpVersionCommandHandler(
            context,
            Accounts(account),
            agent,
            Journal(account, null),
            SitesTestContext.SiteNames(),
            new PhpAccountPoolSettings(context),
            SiteStacks.Ready(),
            locks);
    }

    /// <summary>Builds the options handler.</summary>
    private static ReplaceSiteOptionsCommandHandler OptionsHandler(
        SitesDbContext context, Guid account, IAgentSitesClient agent, ISiteOperationLock locks)
    {
        return new ReplaceSiteOptionsCommandHandler(
            context,
            Accounts(account),
            agent,
            Journal(account, null),
            SitesTestContext.SiteNames(),
            new RecordingLogger<ReplaceSiteOptionsCommandHandler>(),
            SiteStacks.Ready(),
            locks);
    }

    /// <summary>Builds the re-render handler.</summary>
    private static PhpSiteVhostsRerenderRequestedHandler RerenderHandler(
        SitesDbContext context, IAgentSitesClient agent, Guid account, ISiteOperationLock locks)
    {
        return new PhpSiteVhostsRerenderRequestedHandler(
            context,
            agent,
            SiteStacks.Ready(),
            new SitePlanLimits(Accounts(account)),
            new PhpAccountPoolSettings(context),
            new RecordingLogger<PhpSiteVhostsRerenderRequestedHandler>(),
            locks);
    }
}
