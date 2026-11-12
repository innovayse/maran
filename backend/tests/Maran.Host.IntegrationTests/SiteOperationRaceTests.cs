using Maran.Host.IntegrationTests.Fixtures;
using Maran.Modules.Sites.Commands.DisableSite;
using Maran.Modules.Sites.Domain.Entities;
using Maran.Modules.Sites.Domain.Enums;
using Maran.Modules.Sites.IntegrationEvents.Events;
using Maran.Modules.Sites.IntegrationEvents.Handlers;
using Maran.Modules.Sites.Persistence;
using Maran.Modules.Sites.Resources;
using Maran.Modules.Sites.Services;
using Maran.Sdk.Modules;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// A site disable and a plan-change re-render of the same account's vhosts, started in the same instant
/// against real PostgreSQL with the real migrations applied: the re-render never writes a live vhost over
/// the suspension the disable is in the middle of, and it never waits on a site it is not rendering.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this test cannot live in the module's own project.</b> The property is a lock shared by two
/// handlers and a database row read under it; the in-memory provider does not model the second and a unit
/// test that builds both handlers by hand proves only that the test wired them to one lock.
/// </para>
/// <para>
/// <b>How the test is made able to LOSE.</b> <see cref="HeldDisableSitesAgent"/> holds the disable inside
/// the agent call, after the vhost is suspended and before the row is saved, for as long as the test wants.
/// A re-render started in that window reads a row that still says <c>Enabled</c>, so without the site lock
/// it renders: the host then serves a live vhost for a site the panel is about to record as disabled.
/// </para>
/// </remarks>
[Collection(SharedDatabase.Name)]
public sealed class SiteOperationRaceTests : IAsyncLifetime
{
    /// <summary>The account both operations act on.</summary>
    private static readonly Guid AccountId = new("5b1f7c3e-84d2-4a09-9e61-0c27d8a3f4b5");

    /// <summary>The account's system user name.</summary>
    private const string AccountUsername = "acme";

    /// <summary>The longest any waiting step may take before the test calls it a hang.</summary>
    private static readonly TimeSpan Hang = TimeSpan.FromSeconds(20);

    /// <summary>This test's own database on the assembly's shared PostgreSQL server.</summary>
    private readonly TestDatabase _pg;

    /// <summary>The one lock both operations resolve, as the composed panel resolves one.</summary>
    private readonly ParkingSiteLock _locks = new(new SiteOperationLock());

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public SiteOperationRaceTests(PostgresFixture postgres)
    {
        _pg = new TestDatabase(postgres);
    }

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        await _pg.CreateAsync();
        await using var context = OpenContext();
        await context.Database.MigrateAsync();
    }

    /// <inheritdoc />
    public Task DisposeAsync()
    {
        return Task.CompletedTask;
    }

    /// <summary>A re-render that meets a disable in flight waits for it, then finds the site disabled and leaves it alone.</summary>
    /// <remarks>
    /// The defect this closes: the re-render read the row between the agent suspending the vhost and the row
    /// being saved, saw an enabled site, and wrote a live vhost over the suspension. The empty render list is
    /// the whole claim; the disabled row proves the disable itself was not harmed by the wait.
    /// </remarks>
    [Fact]
    public async Task A_rerender_that_meets_a_disable_in_flight_waits_and_then_leaves_the_disabled_site_alone()
    {
        var siteId = await SeedAsync("held.example");
        var agent = new HeldDisableSitesAgent();

        var disable = Task.Run(() =>
        {
            return DisableAsync(agent, siteId);
        });
        await agent.DisableEntered.WaitAsync(Hang);

        var rerender = Task.Run(() =>
        {
            return RerenderAsync(agent);
        });

        // The re-render is parked inside the lock - an event, not a pause - so it has demonstrably not
        // reached the agent; a lock that let it through would never park and this wait would time out.
        await _locks.Parked.WaitAsync(Hang);

        Assert.False(rerender.IsCompleted);
        Assert.Empty(agent.Renders);

        agent.ReleaseDisable();
        var disabled = await disable.WaitAsync(Hang);
        await rerender.WaitAsync(Hang);

        Assert.True(disabled.IsSuccess);
        Assert.Empty(agent.Renders);
        await using var reread = OpenContext();
        Assert.Equal(SiteStatus.Disabled, (await reread.Sites.SingleAsync()).Status);
    }

    /// <summary>A re-render renders the account's other sites while one site is held, and does not render the held one.</summary>
    /// <remarks>
    /// The inverse control the refusing test above owes (rules/testing.md): a lock keyed wider than the
    /// site, one that serialised the whole account, or a re-render that gave up on the first busy site would
    /// satisfy the test above. The free site is rendered WHILE the disable of the other is still held.
    /// </remarks>
    [Fact]
    public async Task A_rerender_renders_the_other_sites_while_one_site_is_held()
    {
        var freeId = await SeedAsync("a-free.example");
        var heldId = await SeedAsync("b-held.example");
        var agent = new HeldDisableSitesAgent();

        var disable = Task.Run(() =>
        {
            return DisableAsync(agent, heldId);
        });
        await agent.DisableEntered.WaitAsync(Hang);
        var rerender = Task.Run(() =>
        {
            return RerenderAsync(agent);
        });

        await WaitUntilAsync(() => { return agent.Renders.Count == 1; });
        var rendered = Assert.Single(agent.Renders);
        Assert.Equal("a-free.example", rendered.Domain);
        Assert.True(rendered.DisableHeld);

        agent.ReleaseDisable();
        await disable.WaitAsync(Hang);
        await rerender.WaitAsync(Hang);

        Assert.Equal(["a-free.example"], agent.Renders.Select(render => { return render.Domain; }));
        await using var reread = OpenContext();
        Assert.Equal(SiteStatus.Enabled, (await reread.Sites.SingleAsync(site => site.Id == freeId)).Status);
        Assert.Equal(SiteStatus.Disabled, (await reread.Sites.SingleAsync(site => site.Id == heldId)).Status);
    }

    /// <summary>With nothing holding the site the same re-render renders it.</summary>
    /// <remarks>
    /// The accepting control: a re-render that skipped every site, or a lock never released, would satisfy
    /// both tests above.
    /// </remarks>
    [Fact]
    public async Task With_nothing_holding_the_site_the_same_rerender_renders_it()
    {
        await SeedAsync("free.example");
        var agent = new HeldDisableSitesAgent();

        await RerenderAsync(agent).WaitAsync(Hang);

        var rendered = Assert.Single(agent.Renders);
        Assert.Equal("free.example", rendered.Domain);
        Assert.False(rendered.DisableHeld);
    }

    /// <summary>Runs the disable on its own connection, as a panel request would.</summary>
    /// <param name="agent">The agent double both operations share.</param>
    /// <param name="siteId">The site to disable.</param>
    /// <returns>What the handler answered.</returns>
    private async Task<Result<Maran.Modules.Sites.Common.SiteDto>> DisableAsync(HeldDisableSitesAgent agent, Guid siteId)
    {
        await using var context = OpenContext();
        var handler = new DisableSiteCommandHandler(
            context,
            new OneAccountDirectory(AccountId, AccountUsername),
            agent,
            new SiteAuditJournal(new DiscardingAuditWriter(), new AccountCustomer(AccountId)),
            new SiteDisplayNames(new StringLocalizer<DisplayNames>(new ResourceManagerStringLocalizerFactory(
                new OptionsWrapper<LocalizationOptions>(new LocalizationOptions()),
                NullLoggerFactory.Instance))),
            new SiteStackGate(new ReadyStackStatus()),
            _locks);

        return await handler.HandleAsync(new DisableSiteCommand(siteId, "203.0.113.7", "tests"), CancellationToken.None);
    }

    /// <summary>Runs the plan-change re-render of the account's vhosts on its own connection, as the queued message would.</summary>
    /// <param name="agent">The agent double both operations share.</param>
    /// <returns>A task that completes when the pass is over.</returns>
    private async Task RerenderAsync(HeldDisableSitesAgent agent)
    {
        await using var context = OpenContext();
        var handler = new PhpSiteVhostsRerenderRequestedHandler(
            context,
            agent,
            new SiteStackGate(new ReadyStackStatus()),
            new SitePlanLimits(new OneAccountDirectory(AccountId, AccountUsername)),
            new PhpAccountPoolSettings(context),
            NullLogger<PhpSiteVhostsRerenderRequestedHandler>.Instance,
            _locks);

        await handler.HandleAsync(new PhpSiteVhostsRerenderRequested(AccountId), CancellationToken.None);
    }

    /// <summary>Stores one enabled PHP site for the account.</summary>
    /// <param name="domain">The site's primary domain.</param>
    /// <returns>The new site's identity.</returns>
    private async Task<Guid> SeedAsync(string domain)
    {
        await using var context = OpenContext();
        var site = new Site(
            Guid.NewGuid(),
            AccountId,
            domain,
            [],
            SiteBackendType.Php,
            "8.3",
            string.Empty,
            string.Empty,
            0,
            string.Empty,
            $"/home/{AccountUsername}/web/{domain}/public",
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        context.Sites.Add(site);
        await context.SaveChangesAsync();

        return site.Id;
    }

    /// <summary>Polls until a condition holds, failing the test if it never does.</summary>
    /// <param name="condition">The condition to wait for.</param>
    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(Hang);
        while (!condition())
        {
            await Task.Delay(20, timeout.Token);
        }
    }

    /// <summary>Opens a context on this test's own database.</summary>
    /// <returns>The context, which the caller disposes.</returns>
    private SitesDbContext OpenContext()
    {
        var options = new DbContextOptionsBuilder<SitesDbContext>()
            .UseNpgsql(_pg.GetConnectionString())
            .Options;

        return new SitesDbContext(options, new AccountCustomer(AccountId));
    }
}
