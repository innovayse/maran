using Maran.Agent.Client.Services.SitesService;
using Maran.Modules.Sites.Domain.Entities;
using Maran.Modules.Sites.Domain.Enums;
using Maran.Modules.Sites.Persistence;
using Maran.Modules.Sites.Services;
using Maran.Modules.Sites.Tests.TestSupport;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Sites.Tests.Services;

/// <summary>
/// What the panel asks the agent to do for its PHP sites when it starts. A pool and a vhost written
/// by an older agent name a socket directory that no longer exists, and this pass is the only thing
/// that moves them.
/// </summary>
public sealed class StartupPhpSiteReconcilerTests
{
    /// <summary>The account owning the first seeded sites.</summary>
    private static readonly Guid FirstAccount = new("a1111111-1111-4111-8111-111111111111");

    /// <summary>The account owning the second seeded sites.</summary>
    private static readonly Guid SecondAccount = new("b2222222-2222-4222-8222-222222222222");

    /// <summary>Every PHP site is converged, whichever account owns it.</summary>
    [Fact]
    public async Task Every_php_site_is_converged_whichever_account_owns_it()
    {
        // The pass runs with no principal, so a tenant-filtered read would see no site at all and
        // look exactly like a host with none. Dropping the filter bypass turns this red.
        using var world = World(
            [Account(FirstAccount, "alpha"), Account(SecondAccount, "beta")],
            Php(FirstAccount, "one.example"),
            Php(SecondAccount, "two.example"));

        Assert.True(await world.Reconciler.ReconcileAsync(CancellationToken.None));

        Assert.Equal(
            ["change-php:alpha:one.example:8.3:False", "change-php:beta:two.example:8.3:False"],
            world.Agent.Calls.Order().ToList());
    }

    /// <summary>The stored version is passed and no previous pool is removed.</summary>
    [Fact]
    public async Task The_stored_version_is_passed_and_no_previous_pool_is_removed()
    {
        // Nothing is being left behind, so a removal here would delete a pool another site of the
        // account still serves from.
        using var world = World([Account(FirstAccount, "alpha")], Php(FirstAccount, "one.example", "8.1"));

        await world.Reconciler.ReconcileAsync(CancellationToken.None);

        Assert.Equal("change-php:alpha:one.example:8.1:False", Assert.Single(world.Agent.Calls));
    }

    /// <summary>The descriptor is the stored row's own and the budget is the plan's.</summary>
    [Fact]
    public async Task The_descriptor_is_the_stored_rows_own_and_the_budget_is_the_plans()
    {
        var site = Php(FirstAccount, "one.example", "8.3", "www.one.example");
        using var world = World([Account(FirstAccount, "alpha")], site);

        await world.Reconciler.ReconcileAsync(CancellationToken.None);

        var descriptor = Assert.Single(world.Agent.Descriptors);
        Assert.Equal(SiteBackendKind.Php, descriptor.Backend);
        Assert.Equal(["www.one.example"], descriptor.Aliases);
        Assert.Equal(site.HasCertificate, descriptor.HasCertificate);
        Assert.Equal(3u, Assert.Single(world.Agent.MaxChildren));
    }

    /// <summary>Static and reverse-proxy sites are not touched.</summary>
    [Fact]
    public async Task Static_and_reverse_proxy_sites_are_not_touched()
    {
        using var world = World(
            [Account(FirstAccount, "alpha")],
            Other(FirstAccount, "static.example", SiteBackendType.Static),
            Other(FirstAccount, "proxy.example", SiteBackendType.ReverseProxy));

        Assert.True(await world.Reconciler.ReconcileAsync(CancellationToken.None));

        Assert.Empty(world.Agent.Calls);
    }

    /// <summary>A site whose account is missing is skipped and does not fail the pass.</summary>
    [Fact]
    public async Task A_site_whose_account_is_missing_is_skipped_and_does_not_fail_the_pass()
    {
        // A retry cannot create the account, so counting this as a failure would repeat the pass
        // five times for nothing.
        using var world = World(
            [Account(FirstAccount, "alpha")],
            Php(SecondAccount, "orphan.example"),
            Php(FirstAccount, "one.example"));

        Assert.True(await world.Reconciler.ReconcileAsync(CancellationToken.None));

        Assert.Equal("change-php:alpha:one.example:8.3:False", Assert.Single(world.Agent.Calls));
        Assert.Contains(world.Log.Entries, entry =>
        {
            return entry.Message.Contains("orphan.example", StringComparison.Ordinal);
        });
    }

    /// <summary>One refused site does not stop the rest, and the pass reports failure.</summary>
    [Fact]
    public async Task One_refused_site_does_not_stop_the_rest_and_the_pass_reports_failure()
    {
        using var world = World(
            [Account(FirstAccount, "alpha")],
            Php(FirstAccount, "a.example"),
            Php(FirstAccount, "b.example"),
            Php(FirstAccount, "c.example"));
        world.Agent.ChangePhpFailingDomains.Add("b.example");

        Assert.False(await world.Reconciler.ReconcileAsync(CancellationToken.None));

        Assert.Equal(3, world.Agent.Calls.Count);
    }

    /// <summary>A site whose agent call throws is counted as failed, the pass continues, and it reports failure.</summary>
    [Fact]
    public async Task A_site_whose_agent_call_throws_is_counted_as_failed_and_the_pass_continues()
    {
        // A thrown fault (agent down mid-pass) must not escape the loop: the sites after it still
        // need converging, and the failure must reach the retry loop as false.
        using var world = World(
            [Account(FirstAccount, "alpha")],
            Php(FirstAccount, "a.example"),
            Php(FirstAccount, "b.example"),
            Php(FirstAccount, "c.example"));
        world.Agent.ChangePhpThrowingDomains.Add("b.example");

        Assert.False(await world.Reconciler.ReconcileAsync(CancellationToken.None));

        Assert.Equal(3, world.Agent.Calls.Count);
        Assert.Contains(world.Log.Entries, entry =>
        {
            return entry.Message
                == "Converged 2 of 3 PHP sites to the current pool and vhost layout; "
                + "1 failed and 0 were skipped because their account is missing";
        });
    }

    /// <summary>The completed pass reports how many sites were converged, failed and skipped.</summary>
    [Fact]
    public async Task The_completed_pass_reports_how_many_sites_were_converged_failed_and_skipped()
    {
        using var world = World(
            [Account(FirstAccount, "alpha")],
            Php(FirstAccount, "a.example"),
            Php(FirstAccount, "b.example"));
        world.Agent.ChangePhpFailingDomains.Add("b.example");

        await world.Reconciler.ReconcileAsync(CancellationToken.None);

        Assert.Contains(world.Log.Entries, entry =>
        {
            return entry.Message
                == "Converged 1 of 2 PHP sites to the current pool and vhost layout; "
                + "1 failed and 0 were skipped because their account is missing";
        });
    }

    /// <summary>A host with no PHP sites succeeds without calling the agent.</summary>
    [Fact]
    public async Task A_host_with_no_php_sites_succeeds_without_calling_the_agent()
    {
        using var world = World([Account(FirstAccount, "alpha")]);

        Assert.True(await world.Reconciler.ReconcileAsync(CancellationToken.None));

        Assert.Empty(world.Agent.Calls);
    }

    /// <summary>The retry bound is finite and spaced, so a broken host is reported rather than hammered.</summary>
    [Fact]
    public void The_retry_bound_is_finite_and_spaced()
    {
        // The loop itself waits RetryDelay between attempts, so driving it would need a real
        // thirty-second sleep, which rules/testing.md forbids. The pass it repeats is asserted above
        // (a refusal reports false); this pins the numbers the loop reads.
        Assert.Equal(5, StartupPhpSiteReconciler.MaximumAttempts);
        Assert.Equal(TimeSpan.FromSeconds(30), StartupPhpSiteReconciler.RetryDelay);
    }

    /// <summary>Builds a world with the given accounts and sites.</summary>
    private static ReconcilerWorld World(IReadOnlyList<AccountSnapshot> accounts, params Site[] sites)
    {
        return new ReconcilerWorld(accounts, sites);
    }

    /// <summary>Builds an account snapshot with a three-worker pool budget.</summary>
    private static AccountSnapshot Account(Guid id, string username)
    {
        return new AccountSnapshot(id, username, 5, 5, 5, 5, 3, 1024, AccountState.Active);
    }

    /// <summary>Builds a PHP site.</summary>
    private static Site Php(Guid accountId, string domain, string version = "8.3", params string[] aliases)
    {
        return SitesTestContext.PhpSite(accountId, domain, version, aliases);
    }

    /// <summary>Builds a site of another backend.</summary>
    private static Site Other(Guid accountId, string domain, SiteBackendType backend)
    {
        return new Site(
            Guid.NewGuid(),
            accountId,
            domain,
            [],
            backend,
            string.Empty,
            backend == SiteBackendType.ReverseProxy ? "127.0.0.1:8080" : string.Empty,
            $"/home/acct/sites/{domain}",
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
    }

    /// <summary>The reconciler wired over fakes, with the stores it reads.</summary>
    private sealed class ReconcilerWorld : IDisposable
    {
        /// <summary>The context every scope resolves.</summary>
        private readonly SitesDbContext _dbContext;

        /// <summary>The container the reconciler's scopes come from.</summary>
        private readonly StartupPhpSiteScopeFactory _scopes;

        /// <summary>The agent double.</summary>
        public RecordingAgentSitesClient Agent { get; } = new();

        /// <summary>Keeps the lines the pass reports.</summary>
        public RecordingLogger<StartupPhpSiteReconciler> Log { get; } = new();

        /// <summary>The reconciler under test.</summary>
        public StartupPhpSiteReconciler Reconciler { get; }

        /// <summary>Seeds the store and builds the reconciler over it.</summary>
        /// <param name="accounts">The accounts the directory knows.</param>
        /// <param name="sites">The rows the panel stores.</param>
        public ReconcilerWorld(IReadOnlyList<AccountSnapshot> accounts, IReadOnlyList<Site> sites)
        {
            // Seeded as an administrator and read back with no principal at all, which is what
            // startup has.
            _dbContext = SitesTestContext.Create(FakeCurrentUser.Admin());
            _dbContext.Sites.AddRange(sites);
            _dbContext.SaveChanges();

            _scopes = new StartupPhpSiteScopeFactory(_dbContext, Agent, new StubAccountDirectory(accounts.ToArray()));
            Reconciler = new StartupPhpSiteReconciler(_scopes.Scopes, Log);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            Reconciler.Dispose();
            _scopes.Dispose();
            _dbContext.Dispose();
        }
    }
}
