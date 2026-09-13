using Maran.Host.IntegrationTests.Fixtures;
using Maran.Modules.Sftp.Commands.CreateSftpUser;
using Maran.Modules.Sftp.Persistence;
using Maran.Modules.Sftp.Services;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// Two SFTP logins created in the same instant, against real PostgreSQL with the real migrations
/// applied: the account never ends up over its plan, and the request that loses is told what happened.
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
/// <see cref="BarrierSftpAgent"/> until both have arrived there — which is to say until both have passed
/// the handler's pre-agent count check believing they had room. Without that, the two tasks would be
/// serialised by chance and the test would pass over a handler with no protection at all.
/// </para>
/// </remarks>
[Collection(SharedDatabase.Name)]
public sealed class SftpUserLimitRaceTests : IAsyncLifetime
{
    /// <summary>The account both requests in these tests create logins for.</summary>
    private static readonly Guid AccountId = new("2f7d4a91-5c3e-4b08-9ad6-71e2c45b8f03");

    /// <summary>The account's system user name, which the agent prefixes each login with.</summary>
    private const string AccountUsername = "acme";

    /// <summary>This test's own database on the assembly's shared PostgreSQL server.</summary>
    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public SftpUserLimitRaceTests(PostgresFixture postgres)
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
    /// account ended one login over what it pays for. The row count is the assertion that matters; the
    /// success count is what proves the two requests really did contend, rather than one of them having
    /// failed for some unrelated reason.
    /// </remarks>
    [Fact]
    public async Task Two_creations_in_the_same_instant_cannot_take_an_account_past_a_plan_allowing_one()
    {
        var agent = new BarrierSftpAgent(expectedCallers: 2);

        var results = await Task.WhenAll(
            CreateAsync(agent, "deploy", maxSftpUsers: 1),
            CreateAsync(agent, "backup", maxSftpUsers: 1));

        await using var reread = OpenContext();
        Assert.Equal(1, await reread.SftpUsers.CountAsync(CancellationToken.None));
        Assert.Equal(1, results.Count(result => { return result.IsSuccess; }));
        Assert.Equal(2, agent.CreateCalls);
    }

    /// <summary>The creation that loses the race is told the plan filled up, not that the name is taken.</summary>
    /// <remarks>
    /// A refusal reading "conflict" or "that name is taken" sends the customer to rename a login whose
    /// name was never the problem. This one says the plan filled up, which is the condition, and its
    /// remedy — delete a login you no longer need — is the only thing that helps. The delete count is the
    /// other half: the login the loser had already made on the host must not be left behind, because
    /// this module keeps no copy of its password and nobody could ever use or repair it.
    /// </remarks>
    [Fact]
    public async Task The_creation_that_loses_the_race_is_told_the_plan_filled_up_and_leaves_no_login_behind()
    {
        var agent = new BarrierSftpAgent(expectedCallers: 2);

        var results = await Task.WhenAll(
            CreateAsync(agent, "deploy", maxSftpUsers: 1),
            CreateAsync(agent, "backup", maxSftpUsers: 1));

        var loser = Assert.Single(results, result => { return !result.IsSuccess; });
        Assert.Equal("SftpUserLimitReachedConcurrently", loser.Error!.Code);
        Assert.Equal(ErrorType.Conflict, loser.Error.Type);
        Assert.Equal(1, agent.DeleteCalls);
    }

    /// <summary>Two creations inside an allowance of two both succeed.</summary>
    /// <remarks>
    /// The INVERSE CONTROL the two refusing tests owe (rules/testing.md): a gate mutated to refuse every
    /// claim, or a lock that deadlocked two contending requests, would satisfy every assertion above —
    /// the row count would be one or zero and a refusal would always be present. This feeds the same
    /// contended path an allowance it must ACCEPT, and asserts BOTH rows exist and nothing was
    /// compensated.
    /// </remarks>
    [Fact]
    public async Task Two_creations_inside_an_allowance_of_two_both_succeed()
    {
        var agent = new BarrierSftpAgent(expectedCallers: 2);

        var results = await Task.WhenAll(
            CreateAsync(agent, "deploy", maxSftpUsers: 2),
            CreateAsync(agent, "backup", maxSftpUsers: 2));

        Assert.All(results, result => { Assert.True(result.IsSuccess); });

        await using var reread = OpenContext();
        Assert.Equal(2, await reread.SftpUsers.CountAsync(CancellationToken.None));
        Assert.Equal(0, agent.DeleteCalls);
    }

    /// <summary>A single creation beyond the allowance is still refused before the host is touched.</summary>
    /// <remarks>
    /// The uncontended refusal, asserted here as well as in the module's unit tests because it is what
    /// keeps the compensating path rare: if the pre-agent check had been dropped in favour of the gate,
    /// every over-limit request would create a login on the host and delete it again. The zero create
    /// count is the whole claim.
    /// </remarks>
    [Fact]
    public async Task A_creation_beyond_the_allowance_is_refused_before_the_host_is_touched()
    {
        var first = new BarrierSftpAgent(expectedCallers: 1);
        Assert.True((await CreateAsync(first, "deploy", maxSftpUsers: 1)).IsSuccess);

        var second = new BarrierSftpAgent(expectedCallers: 1);
        var result = await CreateAsync(second, "backup", maxSftpUsers: 1);

        Assert.False(result.IsSuccess);
        Assert.Equal("SftpUserLimitReached", result.Error!.Code);
        Assert.Equal(0, second.CreateCalls);
    }

    /// <summary>Runs one creation on its own connection, as a panel request would.</summary>
    /// <param name="agent">The agent double both concurrent creations share.</param>
    /// <param name="name">The login suffix the customer asked for.</param>
    /// <param name="maxSftpUsers">The plan's SFTP allowance.</param>
    /// <returns>What the handler answered.</returns>
    /// <remarks>
    /// A context of its own per call, because two requests are two connections and the advisory lock this
    /// test exists to measure is taken on a connection. Sharing one <c>DbContext</c> between the two tasks
    /// would serialise them inside EF Core and measure nothing about the database.
    /// </remarks>
    private async Task<Result<Maran.Modules.Sftp.Common.CreatedSftpUserDto>> CreateAsync(
        BarrierSftpAgent agent,
        string name,
        int maxSftpUsers)
    {
        await using var context = OpenContext();

        var principal = new AccountCustomer(AccountId);
        var handler = new CreateSftpUserCommandHandler(
            context,
            new OneAccountDirectory(AccountId, AccountUsername, maxSftpUsers: maxSftpUsers),
            agent,
            new SftpUserSlotGate(context),
            new SftpAuditJournal(new DiscardingAuditWriter(), principal),
            new FixedInstantClock(),
            NullLogger<CreateSftpUserCommandHandler>.Instance);

        return await handler.HandleAsync(new CreateSftpUserCommand(AccountId, name), CancellationToken.None);
    }

    /// <summary>Opens a context on this test's own database.</summary>
    /// <returns>The context, which the caller disposes.</returns>
    private SftpDbContext OpenContext()
    {
        var options = new DbContextOptionsBuilder<SftpDbContext>()
            .UseNpgsql(_pg.GetConnectionString())
            .Options;

        return new SftpDbContext(options, new AccountCustomer(AccountId));
    }
}
