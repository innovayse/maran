using Maran.Agent.Client.Interfaces;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Modules.Sites.Domain.Entities;
using Maran.Modules.Sites.Domain.Enums;
using Maran.Modules.Sites.Persistence;
using Maran.Modules.Sites.Services;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Maran.SharedKernel.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// The startup PHP convergence pass against a real PostgreSQL, where the Sites tenant filter is the
/// real one.
/// </summary>
/// <remarks>
/// Why this is an integration test: the pass runs with NO principal, and the only thing that makes
/// it see any row is the filter bypass on its read. The in-memory provider the unit tests use
/// honours that bypass too, but the migrated schema, the real filter expression and the real
/// provider's translation of the backend predicate are exactly what a unit test cannot vouch for.
/// </remarks>
[Collection(SharedDatabase.Name)]
public sealed class StartupPhpSiteReconcilerTests : IAsyncLifetime
{
    /// <summary>The account owning the first seeded site.</summary>
    private static readonly Guid FirstAccount = new("a1111111-1111-4111-8111-111111111111");

    /// <summary>The account owning the second seeded sites.</summary>
    private static readonly Guid SecondAccount = new("b2222222-2222-4222-8222-222222222222");

    /// <summary>The PostgreSQL this class runs against.</summary>
    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public StartupPhpSiteReconcilerTests(PostgresFixture postgres)
    {
        _pg = new TestDatabase(postgres);
    }

    /// <summary>Creates this test's database and applies the Sites migrations.</summary>
    /// <returns>Resolves once the schema exists.</returns>
    public async Task InitializeAsync()
    {
        await _pg.CreateAsync();
        await using var context = OpenContext(new NoPrincipal());
        await context.Database.MigrateAsync();
    }

    /// <summary>Releases nothing; the shared server outlives the test.</summary>
    /// <returns>A completed task.</returns>
    public Task DisposeAsync()
    {
        return Task.CompletedTask;
    }

    /// <summary>The sites of two accounts both reach the agent although the pass has no principal.</summary>
    [Fact]
    public async Task Sites_of_two_accounts_both_reach_the_agent_with_no_principal()
    {
        await SeedAsync(
            SiteRow(FirstAccount, "one.example", SiteBackendType.Php, "8.3"),
            SiteRow(SecondAccount, "two.example", SiteBackendType.Php, "8.2"),
            SiteRow(SecondAccount, "static.example", SiteBackendType.Static, string.Empty));
        var agent = new PhpConvergeRecordingAgent();

        using var provider = Provider(agent);
        var reconciler = new StartupPhpSiteReconciler(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<StartupPhpSiteReconciler>.Instance);

        Assert.True(await reconciler.ReconcileAsync(CancellationToken.None));

        Assert.Equal(
            ["alpha:one.example:8.3:False", "beta:two.example:8.2:False"],
            agent.Calls.Order().ToList());
    }

    /// <summary>Seeds the given rows through a context with no principal.</summary>
    /// <param name="sites">The rows to store.</param>
    private async Task SeedAsync(params Site[] sites)
    {
        await using var seed = OpenContext(new NoPrincipal());
        seed.Sites.AddRange(sites);
        await seed.SaveChangesAsync();
    }

    /// <summary>Builds a site row.</summary>
    /// <param name="accountId">The owning account.</param>
    /// <param name="domain">The primary domain.</param>
    /// <param name="backend">The backend kind.</param>
    /// <param name="version">The stored PHP version, empty for a non-PHP site.</param>
    private static Site SiteRow(Guid accountId, string domain, SiteBackendType backend, string version)
    {
        return new Site(
            Guid.NewGuid(),
            accountId,
            domain,
            [],
            backend,
            version,
            string.Empty,
            $"/home/acct/sites/{domain}",
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
    }

    /// <summary>Builds the container one pass resolves its dependencies from.</summary>
    /// <param name="agent">The agent double.</param>
    private ServiceProvider Provider(IAgentSitesClient agent)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICurrentUser>(new NoPrincipal());
        services.AddDbContext<SitesDbContext>(options =>
        {
            options.UseNpgsql(_pg.GetConnectionString());
        });
        services.AddSingleton(agent);
        services.AddSingleton<IAccountDirectory>(new FixedAccountDirectory(
            new AccountSnapshot(FirstAccount, "alpha", 5, 5, 5, 5, 3, 1024, AccountState.Active),
            new AccountSnapshot(SecondAccount, "beta", 5, 5, 5, 5, 3, 1024, AccountState.Active)));
        return services.BuildServiceProvider();
    }

    /// <summary>Opens a Sites context seen as <paramref name="user"/>.</summary>
    /// <param name="user">The principal the tenant filter closes over.</param>
    private SitesDbContext OpenContext(ICurrentUser user)
    {
        var options = new DbContextOptionsBuilder<SitesDbContext>()
            .UseNpgsql(_pg.GetConnectionString())
            .Options;

        return new SitesDbContext(options, user);
    }
}
