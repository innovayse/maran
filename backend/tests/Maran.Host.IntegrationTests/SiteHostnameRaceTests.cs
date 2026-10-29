using Maran.Host.IntegrationTests.Fixtures;
using Maran.Modules.Sites.Commands.CreateSite;
using Maran.Modules.Sites.Common;
using Maran.Modules.Sites.Domain.Entities;
using Maran.Modules.Sites.Domain.Enums;
using Maran.Modules.Sites.Persistence;
using Maran.Modules.Sites.Services;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// Two tenants creating sites that name one host in the same instant, against real PostgreSQL with the
/// real migrations applied: exactly one site wins the name, the loser is told the domain is taken, and
/// the vhost the loser had already written is taken down again.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this lives here.</b> The decision is made by the unique key on <c>SiteHostnames</c>, and the
/// in-memory provider of the unit tests enforces no key, so a mutation that deleted the catch for the
/// violation would leave every unit test green. Two accounts, because accounts do not share the slot
/// gate's per-account advisory lock: the second INSERT really blocks on the first's uncommitted key and
/// then fails with 23505, which is the production interleaving.
/// </para>
/// <para>
/// <b>How the tests are made able to LOSE.</b> <see cref="BarrierSitesAgent"/> holds both creations
/// inside the agent call until both have arrived, which is after both passed the handler's pre-check
/// believing the name free. Without it the tasks would be serialised by chance.
/// </para>
/// <para>
/// The agent-side twin of these tests is the polygon suite
/// <c>agent/crates/agent/tests/sites_on_a_real_host.rs</c>, where the agent refuses a name another vhost
/// serves and writes nothing. The contract between the two: after that refusal (<c>AgentAlreadyExists</c>
/// here) the panel deletes NOTHING, because a delete by domain would remove the winner's vhost.
/// </para>
/// </remarks>
[Collection(SharedDatabase.Name)]
public sealed class SiteHostnameRaceTests : IAsyncLifetime
{
    /// <summary>The first tenant's account.</summary>
    private static readonly Guid FirstAccount = new("a1f0c3d2-5b6e-4a78-9c01-2d3e4f5a6b71");

    /// <summary>The second tenant's account.</summary>
    private static readonly Guid SecondAccount = new("b2e1d4c3-6c7f-4b89-8d12-3e4f5a6b7c82");

    /// <summary>This test's own database on the assembly's shared PostgreSQL server.</summary>
    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public SiteHostnameRaceTests(PostgresFixture postgres)
    {
        _pg = new TestDatabase(postgres);
    }

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        await _pg.CreateAsync();
        await using var context = OpenContext(FirstAccount);
        await context.Database.MigrateAsync();
    }

    /// <inheritdoc />
    public Task DisposeAsync()
    {
        return Task.CompletedTask;
    }

    /// <summary>Two tenants racing for one alias end with one site and one taken domain.</summary>
    /// <remarks>
    /// The defect: the second creation hit the key, the exception left the handler as a 500, and the
    /// vhost it had written stayed on the host. The row count says one site exists; the failure code
    /// says the loser was answered, not faulted.
    /// </remarks>
    [Fact]
    public async Task Two_tenants_racing_for_one_alias_end_with_one_site_and_one_taken_domain()
    {
        var agent = new BarrierSitesAgent(expectedCallers: 2);

        var results = await Task.WhenAll(
            CreateAsync(agent, FirstAccount, "first", "shop.example", []),
            CreateAsync(agent, SecondAccount, "second", "zero.example", ["shop.example"]));

        var loser = Assert.Single(results, result => { return !result.IsSuccess; });
        Assert.Equal("SiteDomainTaken", loser.Error!.Code);
        Assert.Equal(ErrorType.Conflict, loser.Error.Type);
        Assert.Equal(1, results.Count(result => { return result.IsSuccess; }));

        await using var reread = OpenContext(FirstAccount);
        Assert.Equal(1, await reread.Sites.IgnoreQueryFilters().CountAsync(CancellationToken.None));
        var claims = await reread.SiteHostnames.IgnoreQueryFilters().Select(hostname => hostname.Name).ToListAsync();
        Assert.Equal(claims.Count, claims.Distinct().Count());
    }

    /// <summary>The loser of a hostname race removes the vhost it had already written.</summary>
    /// <remarks>
    /// The defect: no row owns the loser's vhost, yet it is live and lists the winner's hostname, so
    /// whichever file sorts first answers for it. The deleted domain must be the LOSER's own, and the
    /// delete must retire no php-fpm pool (the account's other sites share it).
    /// </remarks>
    [Fact]
    public async Task The_loser_of_a_hostname_race_removes_the_vhost_it_had_already_written()
    {
        var agent = new BarrierSitesAgent(expectedCallers: 2);

        var results = await Task.WhenAll(
            CreateAsync(agent, FirstAccount, "first", "shop.example", []),
            CreateAsync(agent, SecondAccount, "second", "zero.example", ["shop.example"]));

        var loserDomain = results[0].IsSuccess ? "zero.example" : "shop.example";
        Assert.Equal([loserDomain], agent.DeletedDomains);
        Assert.Equal([string.Empty], agent.RetiredPhpVersions);
    }

    /// <summary>Two creations of one primary domain leave the winner's vhost alone.</summary>
    /// <remarks>
    /// The agent names a vhost after the primary domain alone and deletes by that name, so when both
    /// requests name the same domain the loser's compensating delete would remove the WINNER's file.
    /// The empty delete list is the whole claim.
    /// </remarks>
    [Fact]
    public async Task Two_creations_of_one_primary_domain_leave_the_winners_vhost_alone()
    {
        var agent = new BarrierSitesAgent(expectedCallers: 2);

        var results = await Task.WhenAll(
            CreateAsync(agent, FirstAccount, "first", "shop.example", []),
            CreateAsync(agent, SecondAccount, "second", "shop.example", []));

        var loser = Assert.Single(results, result => { return !result.IsSuccess; });
        Assert.Equal("SiteDomainTaken", loser.Error!.Code);
        Assert.Equal(1, results.Count(result => { return result.IsSuccess; }));
        Assert.Empty(agent.DeletedDomains);

        await using var reread = OpenContext(FirstAccount);
        Assert.Equal(1, await reread.Sites.IgnoreQueryFilters().CountAsync(CancellationToken.None));
    }

    /// <summary>Two creations with disjoint names both succeed and nothing is removed.</summary>
    /// <remarks>
    /// The INVERSE CONTROL (rules/testing.md): a handler that refused or compensated on every contended
    /// save would satisfy every assertion above. This feeds the same contended path names it must ACCEPT.
    /// </remarks>
    [Fact]
    public async Task Two_creations_with_disjoint_names_both_succeed_and_nothing_is_removed()
    {
        var agent = new BarrierSitesAgent(expectedCallers: 2);

        var results = await Task.WhenAll(
            CreateAsync(agent, FirstAccount, "first", "shop.example", ["www.shop.example"]),
            CreateAsync(agent, SecondAccount, "second", "other.example", ["www.other.example"]));

        Assert.All(results, result => { Assert.True(result.IsSuccess); });
        Assert.Empty(agent.DeletedDomains);

        await using var reread = OpenContext(FirstAccount);
        Assert.Equal(2, await reread.Sites.IgnoreQueryFilters().CountAsync(CancellationToken.None));
        Assert.Equal(4, await reread.SiteHostnames.IgnoreQueryFilters().CountAsync(CancellationToken.None));
    }

    /// <summary>A refusal by the agent as already served is answered domain taken and deletes nothing.</summary>
    /// <remarks>
    /// The panel half of the cross-stack contract: the agent answers a host-side name collision with
    /// <c>AlreadyExists</c> having written nothing, so a delete here would remove another site's vhost.
    /// Its twin on the agent side is the polygon test that refuses a name another vhost serves.
    /// </remarks>
    [Fact]
    public async Task A_creation_the_agent_refuses_as_already_served_is_answered_domain_taken_and_deletes_nothing()
    {
        var agent = new BarrierSitesAgent(expectedCallers: 1)
        {
            RefuseCreationWith = Error.Of("AgentAlreadyExists", ErrorType.Conflict),
        };

        var result = await CreateAsync(agent, FirstAccount, "first", "shop.example", []);

        Assert.False(result.IsSuccess);
        Assert.Equal("SiteDomainTaken", result.Error!.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Empty(agent.DeletedDomains);

        await using var reread = OpenContext(FirstAccount);
        Assert.Equal(0, await reread.Sites.CountAsync(CancellationToken.None));
    }

    /// <summary>The refused creation is journalled as a failed site creation beside the winner's success.</summary>
    /// <remarks>
    /// Definition of Done item 4: the audit entry is written and asserted, for the refusal as well as
    /// the success.
    /// </remarks>
    [Fact]
    public async Task The_refused_creation_is_journalled_as_a_failed_site_creation()
    {
        var agent = new BarrierSitesAgent(expectedCallers: 2);
        var audit = new RecordingAuditWriter();

        var results = await Task.WhenAll(
            CreateAsync(agent, FirstAccount, "first", "shop.example", [], audit),
            CreateAsync(agent, SecondAccount, "second", "zero.example", ["shop.example"], audit));

        var loserDomain = results[0].IsSuccess ? "zero.example" : "shop.example";
        var winnerDomain = results[0].IsSuccess ? "shop.example" : "zero.example";
        Assert.Contains(audit.Entries, entry =>
        {
            return entry.Action == AuditActions.SiteCreated && entry.Subject == loserDomain && !entry.Succeeded;
        });
        Assert.Contains(audit.Entries, entry =>
        {
            return entry.Action == AuditActions.SiteCreated && entry.Subject == winnerDomain && entry.Succeeded;
        });
    }

    /// <summary>A claim the key refuses leaves nothing tracked for the next save.</summary>
    /// <remarks>
    /// The gate is scoped and its context outlives the failed save only in tests, but a context that
    /// kept the refused site Added would re-insert it on the NEXT save, such as the journal's. The
    /// empty change tracker and the zero-row save are the claim.
    /// </remarks>
    [Fact]
    public async Task A_claim_the_hostname_key_refuses_leaves_nothing_tracked_for_the_next_save()
    {
        await using (var seed = OpenContext(FirstAccount))
        {
            seed.Sites.Add(NewSite(FirstAccount, "shop.example", []));
            await seed.SaveChangesAsync();
        }

        await using var context = OpenContext(SecondAccount);
        var gate = new SiteSlotGate(context);

        var conflict = await Assert.ThrowsAsync<DbUpdateException>(async () =>
        {
            await gate.TryTakeAsync(NewSite(SecondAccount, "zero.example", ["shop.example"]), 5, CancellationToken.None);
        });

        Assert.Equal(
            PostgresErrorCodes.UniqueViolation,
            Assert.IsType<PostgresException>(conflict.InnerException).SqlState);
        Assert.Empty(context.ChangeTracker.Entries());
        Assert.Equal(0, await context.SaveChangesAsync());
    }

    /// <summary>Builds an unsaved STATIC site.</summary>
    /// <param name="accountId">The owning account.</param>
    /// <param name="domain">The primary domain.</param>
    /// <param name="aliases">The aliases.</param>
    /// <returns>The site.</returns>
    private static Site NewSite(Guid accountId, string domain, IReadOnlyList<string> aliases)
    {
        return new Site(
            Guid.NewGuid(),
            accountId,
            domain,
            aliases,
            SiteBackendType.Static,
            string.Empty,
            string.Empty,
            $"/home/acct/web/{domain}/public",
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
    }

    /// <summary>Runs one creation on its own connection as the given tenant, the way a panel request would.</summary>
    /// <param name="agent">The agent double both concurrent creations share.</param>
    /// <param name="accountId">The tenant's account.</param>
    /// <param name="username">The account's system user name.</param>
    /// <param name="domain">The primary domain asked for.</param>
    /// <param name="aliases">The aliases asked for.</param>
    /// <param name="audit">Where the journal writes; discarded when omitted.</param>
    /// <returns>What the handler answered.</returns>
    private async Task<Result<SiteDto>> CreateAsync(
        BarrierSitesAgent agent,
        Guid accountId,
        string username,
        string domain,
        IReadOnlyList<string> aliases,
        IAuditWriter? audit = null)
    {
        await using var context = OpenContext(accountId);

        var principal = new AccountCustomer(accountId);
        var handler = new CreateSiteCommandHandler(
            context,
            new OneAccountDirectory(accountId, username, maxSites: 5),
            agent,
            new ThrowingAgentPhpClient(),
            new SiteSlotGate(context),
            new SiteAuditJournal(audit ?? new DiscardingAuditWriter(), principal),
            new FixedInstantClock(),
            NullLogger<CreateSiteCommandHandler>.Instance);

        return await handler.HandleAsync(
            new CreateSiteCommand(accountId, domain, SiteBackendType.Static, aliases), CancellationToken.None);
    }

    /// <summary>Opens a context on this test's own database as the given tenant.</summary>
    /// <remarks>
    /// The context applies that tenant's query filter, so a reread that counts BOTH tenants' rows calls
    /// <c>IgnoreQueryFilters</c>: which tenant wins a race is up to the scheduler, and a filtered count
    /// would see the winner only when it happened to be the tenant the context was opened as.
    /// </remarks>
    /// <param name="accountId">The tenant whose filter the context applies.</param>
    /// <returns>The context, which the caller disposes.</returns>
    private SitesDbContext OpenContext(Guid accountId)
    {
        var options = new DbContextOptionsBuilder<SitesDbContext>()
            .UseNpgsql(_pg.GetConnectionString())
            .Options;

        return new SitesDbContext(options, new AccountCustomer(accountId));
    }
}
