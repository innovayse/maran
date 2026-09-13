using Maran.Host.IntegrationTests.Fixtures;
using Maran.Modules.Sites.Commands.CreateSite;
using Maran.Modules.Sites.Domain.Enums;
using Maran.Modules.Sites.Persistence;
using Maran.Modules.Sites.Services;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// Two sites created in the same instant, against real PostgreSQL with the real migrations applied: the
/// account never ends up over its plan, and the request that loses is told what happened.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this test cannot live in the module's own project.</b> The protection is a PostgreSQL advisory
/// lock taken inside an explicit transaction, and the in-memory provider the unit tests run on has
/// neither — a mutation deleting the lock would leave every one of them green. So the property is only
/// observable here, over two independent connections to one database, which is also what two panel
/// requests are.
/// </para>
/// <para>
/// <b>How the test is made able to LOSE.</b> Both creations are held inside the agent call by
/// <see cref="BarrierSitesAgent"/> until both have arrived there — which is to say until both have passed
/// the handler's pre-agent count check believing they had room. Without that, the two tasks would be
/// serialised by chance and the test would pass over a handler with no protection at all.
/// </para>
/// <para>
/// Every site here is STATIC, so the handler's PHP-version probe is skipped and no PHP client is needed.
/// That is not a narrowing of what is measured: the limit and the gate are identical for every backend,
/// and the one place the backend matters — which pool a compensating delete may retire — is asserted
/// directly through <see cref="BarrierSitesAgent.RetiredPhpVersions"/>.
/// </para>
/// </remarks>
[Collection(SharedDatabase.Name)]
public sealed class SiteLimitRaceTests : IAsyncLifetime
{
    /// <summary>The account both requests in these tests create sites for.</summary>
    private static readonly Guid AccountId = new("d4e8b072-19af-4c36-85b2-6f0193c7ae41");

    /// <summary>The account's system user name, under whose home the document root is built.</summary>
    private const string AccountUsername = "acme";

    /// <summary>This test's own database on the assembly's shared PostgreSQL server.</summary>
    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public SiteLimitRaceTests(PostgresFixture postgres)
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

    /// <summary>Two creations in the same instant cannot take an account past a plan allowing one.</summary>
    /// <remarks>
    /// The defect this closes: the limit was a count compared against the plan before the agent was
    /// called, with nothing behind the count, so two requests each reading zero both succeeded and the
    /// account ended one site over what it pays for. The row count is the assertion that matters; the
    /// success count is what proves the two requests really did contend.
    /// </remarks>
    [Fact]
    public async Task Two_creations_in_the_same_instant_cannot_take_an_account_past_a_plan_allowing_one()
    {
        var agent = new BarrierSitesAgent(expectedCallers: 2);

        var results = await Task.WhenAll(
            CreateAsync(agent, "first.example", maxSites: 1),
            CreateAsync(agent, "second.example", maxSites: 1));

        await using var reread = OpenContext();
        Assert.Equal(1, await reread.Sites.CountAsync(CancellationToken.None));
        Assert.Equal(1, results.Count(result => { return result.IsSuccess; }));
        Assert.Equal(2, agent.CreateCalls);
    }

    /// <summary>The creation that loses the race is told the plan filled up, not that the domain is taken.</summary>
    /// <remarks>
    /// A refusal reading "conflict" or "that domain is taken" sends the customer to rename a site whose
    /// domain was never the problem — and the two domains here really are different, so a message about
    /// the domain would be false as well as unhelpful. The delete is the other half: the vhost the loser
    /// had already provisioned must not be left behind with no row.
    /// </remarks>
    [Fact]
    public async Task The_creation_that_loses_the_race_is_told_the_plan_filled_up_and_leaves_no_vhost_behind()
    {
        var agent = new BarrierSitesAgent(expectedCallers: 2);

        var results = await Task.WhenAll(
            CreateAsync(agent, "first.example", maxSites: 1),
            CreateAsync(agent, "second.example", maxSites: 1));

        var loser = Assert.Single(results, result => { return !result.IsSuccess; });
        Assert.Equal("SiteLimitReachedConcurrently", loser.Error!.Code);
        Assert.Equal(ErrorType.Conflict, loser.Error.Type);
        Assert.Single(agent.RetiredPhpVersions);
    }

    /// <summary>The compensating delete retires no php-fpm pool, because the account's other sites share it.</summary>
    /// <remarks>
    /// The one thing a compensation here can get catastrophically wrong. A pool belongs to an ACCOUNT and
    /// a version rather than to one site, so retiring the losing site's version would take every other
    /// site of that account on that version off the air — an outage caused by a cleanup. The empty string
    /// is what the agent contract documents as "leave every pool alone", and this asserts it on the wire
    /// rather than trusting the call site.
    /// </remarks>
    [Fact]
    public async Task The_compensating_delete_retires_no_php_pool()
    {
        var agent = new BarrierSitesAgent(expectedCallers: 2);

        await Task.WhenAll(
            CreateAsync(agent, "first.example", maxSites: 1),
            CreateAsync(agent, "second.example", maxSites: 1));

        Assert.Equal(string.Empty, Assert.Single(agent.RetiredPhpVersions));
    }

    /// <summary>Two creations inside an allowance of two both succeed.</summary>
    /// <remarks>
    /// The INVERSE CONTROL the refusing tests owe (rules/testing.md): a gate mutated to refuse every
    /// claim, or a lock that deadlocked two contending requests, would satisfy every assertion above.
    /// This feeds the same contended path an allowance it must ACCEPT, and asserts BOTH rows exist and
    /// nothing was deleted.
    /// </remarks>
    [Fact]
    public async Task Two_creations_inside_an_allowance_of_two_both_succeed()
    {
        var agent = new BarrierSitesAgent(expectedCallers: 2);

        var results = await Task.WhenAll(
            CreateAsync(agent, "first.example", maxSites: 2),
            CreateAsync(agent, "second.example", maxSites: 2));

        Assert.All(results, result => { Assert.True(result.IsSuccess); });

        await using var reread = OpenContext();
        Assert.Equal(2, await reread.Sites.CountAsync(CancellationToken.None));
        Assert.Empty(agent.RetiredPhpVersions);
    }

    /// <summary>A single creation beyond the allowance is still refused before the host is touched.</summary>
    /// <remarks>
    /// The uncontended refusal: if the pre-agent check had been dropped in favour of the gate, every
    /// over-limit request would provision a vhost and remove it again. The zero create count is the whole
    /// claim.
    /// </remarks>
    [Fact]
    public async Task A_creation_beyond_the_allowance_is_refused_before_the_host_is_touched()
    {
        var first = new BarrierSitesAgent(expectedCallers: 1);
        Assert.True((await CreateAsync(first, "first.example", maxSites: 1)).IsSuccess);

        var second = new BarrierSitesAgent(expectedCallers: 1);
        var result = await CreateAsync(second, "second.example", maxSites: 1);

        Assert.False(result.IsSuccess);
        Assert.Equal("SiteLimitReached", result.Error!.Code);
        Assert.Equal(0, second.CreateCalls);
    }

    /// <summary>Runs one creation on its own connection, as a panel request would.</summary>
    /// <param name="agent">The agent double both concurrent creations share.</param>
    /// <param name="domain">The primary domain the customer asked for.</param>
    /// <param name="maxSites">The plan's site allowance.</param>
    /// <returns>What the handler answered.</returns>
    /// <remarks>
    /// A context of its own per call, because two requests are two connections and the advisory lock this
    /// test exists to measure is taken on a connection. Sharing one <c>DbContext</c> between the two tasks
    /// would serialise them inside EF Core and measure nothing about the database.
    /// </remarks>
    private async Task<Result<Maran.Modules.Sites.Common.SiteDto>> CreateAsync(
        BarrierSitesAgent agent,
        string domain,
        int maxSites)
    {
        await using var context = OpenContext();

        var principal = new AccountCustomer(AccountId);
        var handler = new CreateSiteCommandHandler(
            context,
            new OneAccountDirectory(AccountId, AccountUsername, maxSites: maxSites),
            agent,
            new ThrowingAgentPhpClient(),
            new SiteSlotGate(context),
            new SiteAuditJournal(new DiscardingAuditWriter(), principal),
            new FixedInstantClock(),
            NullLogger<CreateSiteCommandHandler>.Instance);

        return await handler.HandleAsync(
            new CreateSiteCommand(AccountId, domain, SiteBackendType.Static, []), CancellationToken.None);
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
