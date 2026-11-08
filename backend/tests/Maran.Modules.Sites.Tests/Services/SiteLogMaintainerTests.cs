using Maran.Agent.Client.Services.SitesService;
using Maran.Modules.Sites.Persistence;
using Maran.Modules.Sites.Services;
using Maran.Modules.Sites.Tests.TestSupport;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Sites.Tests.Services;

/// <summary>Behavioral contract of <see cref="SiteLogMaintainer"/>: read first, prune second, never stall the rest.</summary>
public sealed class SiteLogMaintainerTests
{
    /// <summary>The instant the maintainer believes it is.</summary>
    private static readonly DateTimeOffset Now = new(2026, 11, 7, 9, 30, 0, TimeSpan.Zero);

    /// <summary>The site is read before its archives are pruned.</summary>
    [Fact]
    public async Task The_site_is_read_before_its_archives_are_pruned()
    {
        using var world = await WorldAsync();

        var succeeded = await world.Maintainer().MaintainAsync(world.SiteId, CancellationToken.None);

        Assert.True(succeeded);
        Assert.Equal(["summarise:acme:example.com", "prune:acme:example.com:14"], world.Agent.Calls);
    }

    /// <summary>A read the agent refused leaves the archives alone, so a prune never follows a failed read.</summary>
    [Fact]
    public async Task A_read_the_agent_refused_leaves_the_archives_alone()
    {
        using var world = await WorldAsync();
        world.Agent.TrafficReadings.Enqueue(Result<SiteTrafficReading>.Fail(Error.Of("AgentSaturated", ErrorType.Unavailable)));

        var succeeded = await world.Maintainer().MaintainAsync(world.SiteId, CancellationToken.None);

        Assert.False(succeeded);
        Assert.Equal(["summarise:acme:example.com"], world.Agent.Calls);
    }

    /// <summary>The site's own retention is what the prune is asked for.</summary>
    [Fact]
    public async Task The_sites_own_retention_is_what_the_prune_is_asked_for()
    {
        using var world = await WorldAsync(retention: 2);

        await world.Maintainer().MaintainAsync(world.SiteId, CancellationToken.None);

        Assert.Equal("prune:acme:example.com:2", world.Agent.Calls[^1]);
    }

    /// <summary>A site whose agent call throws is answered false, not thrown, so the next site is still reached.</summary>
    [Fact]
    public async Task A_site_whose_agent_call_throws_is_answered_false_and_not_thrown()
    {
        using var world = await WorldAsync();
        world.Agent.TrafficThrowingDomains.Add("example.com");

        var succeeded = await world.Maintainer().MaintainAsync(world.SiteId, CancellationToken.None);

        Assert.False(succeeded);
        Assert.Contains(world.Log.Entries, entry => { return entry.Level == Microsoft.Extensions.Logging.LogLevel.Error; });
    }

    /// <summary>A site that no longer exists, or whose account is gone, is skipped without calling the agent.</summary>
    [Fact]
    public async Task A_site_that_no_longer_exists_or_whose_account_is_gone_is_skipped_without_calling_the_agent()
    {
        using var world = await WorldAsync(accountKnown: false);

        var orphan = await world.Maintainer().MaintainAsync(world.SiteId, CancellationToken.None);
        var missing = await world.Maintainer().MaintainAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.True(orphan);
        Assert.True(missing);
        Assert.Empty(world.Agent.Calls);
    }

    /// <summary>The unattended pass finds a site although no tenant filter admits it.</summary>
    [Fact]
    public async Task The_unattended_pass_finds_a_site_although_no_tenant_filter_admits_it()
    {
        // The principal is anonymous, so a filtered read would find nothing and look like a host with no sites.
        using var world = await WorldAsync();

        await world.Maintainer().MaintainAsync(world.SiteId, CancellationToken.None);

        await using var anonymous = SitesTestContext.Create(FakeCurrentUser.Anonymous(), world.Database);
        Assert.Empty(await anonymous.Sites.ToListAsync());
        Assert.NotEmpty(world.Agent.Calls);
    }

    /// <summary>Stores a site and builds the objects the maintainer needs.</summary>
    /// <param name="retention">The site's log retention, or null to keep the default.</param>
    /// <param name="accountKnown">Whether the account directory knows the site's account.</param>
    /// <returns>The world the test drives.</returns>
    private static async Task<MaintainWorld> WorldAsync(int? retention = null, bool accountKnown = true)
    {
        var database = Guid.NewGuid().ToString();
        var account = Guid.NewGuid();
        var site = SitesTestContext.PhpSite(account, "example.com");
        if (retention is { } days)
        {
            site.ChangeLogRetention(days);
        }

        await using (var seed = SitesTestContext.Create(FakeCurrentUser.Admin(), database))
        {
            seed.Sites.Add(site);
            await seed.SaveChangesAsync();
        }

        return new MaintainWorld(database, site.Id, accountKnown ? Snapshots.Account(account, "acme") : null);
    }

    /// <summary>One site, the doubles, and a way to build the maintainer over a fresh unattended context.</summary>
    private sealed class MaintainWorld : IDisposable
    {
        /// <summary>The in-memory database name.</summary>
        public string Database { get; }

        /// <summary>The seeded site.</summary>
        public Guid SiteId { get; }

        /// <summary>The agent double.</summary>
        public RecordingAgentSitesClient Agent { get; } = new();

        /// <summary>Where the maintainer reports.</summary>
        public RecordingLogger<SiteLogMaintainer> Log { get; } = new();

        /// <summary>The account directory double.</summary>
        private readonly StubAccountDirectory _accounts;

        /// <summary>The contexts handed to maintainers, disposed with the world.</summary>
        private readonly List<SitesDbContext> _contexts = [];

        /// <summary>Creates the world.</summary>
        /// <param name="database">The in-memory database name.</param>
        /// <param name="siteId">The seeded site.</param>
        /// <param name="account">The account the directory knows, or null for none.</param>
        public MaintainWorld(string database, Guid siteId, Maran.Sdk.Contracts.AccountSnapshot? account)
        {
            Database = database;
            SiteId = siteId;
            _accounts = account is null ? new StubAccountDirectory() : new StubAccountDirectory(account);
        }

        /// <summary>Builds the maintainer over an unattended context.</summary>
        /// <returns>The maintainer.</returns>
        public SiteLogMaintainer Maintainer()
        {
            var context = SitesTestContext.Create(FakeCurrentUser.Anonymous(), Database);
            _contexts.Add(context);
            var audit = new RecordingAuditWriter();

            return new SiteLogMaintainer(
                context,
                _accounts,
                new SiteTrafficIngestor(context, Agent, new FakeClock(Now)),
                new SiteLogPruner(Agent, new SiteAuditJournal(audit, FakeCurrentUser.Anonymous())),
                new SiteLogMaintenanceGate(),
                Log);
        }

        /// <summary>Disposes the contexts the maintainers used.</summary>
        public void Dispose()
        {
            foreach (var context in _contexts)
            {
                context.Dispose();
            }
        }
    }
}
