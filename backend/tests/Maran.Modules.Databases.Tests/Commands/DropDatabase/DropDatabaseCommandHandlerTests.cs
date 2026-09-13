using Maran.Modules.Databases.Commands.DropDatabase;
using Maran.Modules.Databases.Services;
using Maran.Modules.Databases.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Databases.Tests.Commands.DropDatabase;

/// <summary>Which database a drop removes, in what order, and whose it may be.</summary>
public sealed class DropDatabaseCommandHandlerTests
{
    private static readonly Guid OwnerAccountId = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");
    private static readonly Guid StrangerAccountId = Guid.Parse("bbbbbbbb-0000-4000-8000-000000000002");

    /// <summary>Dropping another tenants database answers not found rather than forbidden.</summary>
    [Fact]
    public async Task Dropping_another_tenants_database_answers_not_found_rather_than_forbidden()
    {
        var world = await WorldAsync();

        var result = await world.DropAsync(world.StrangerDatabaseId);

        Assert.False(result.IsSuccess);
        Assert.Equal("DatabaseNotFound", result.Error!.Code);

        // And the server was never touched, which matters more here than on a read: this is the one
        // command whose success would destroy another customer's data irrecoverably.
        Assert.Empty(world.Agent.Drops);
    }

    /// <summary>A drop names the suffixes the row recorded and never derives the user from the database.</summary>
    [Fact]
    public async Task A_drop_names_the_suffixes_the_row_recorded_and_never_derives_the_user_from_the_database()
    {
        // The customer named the two halves independently, so a drop that guessed would either
        // strand a live credential on the server or remove one belonging to another of the account's
        // databases.
        var world = await WorldAsync();

        var result = await world.DropAsync(world.OwnDatabaseId);

        Assert.True(result.IsSuccess);
        var call = Assert.Single(world.Agent.Drops);
        Assert.Equal("alice", call.AccountUsername);
        Assert.Equal("shop", call.DatabaseName);
        Assert.Equal("shopuser", call.DbUsername);
        Assert.NotEqual(call.DatabaseName, call.DbUsername);
    }

    /// <summary>A drop the agent refuses leaves the row in place.</summary>
    [Fact]
    public async Task A_drop_the_agent_refuses_leaves_the_row_in_place()
    {
        // Agent first, row second. A row removed while the database still exists is a customer's
        // data nobody in the panel can see and nobody can now remove.
        var world = await WorldAsync();
        world.Agent.DropResult = Result<bool>.Fail(Error.Of("AgentSystemFailure", ErrorType.Failure));

        var result = await world.DropAsync(world.OwnDatabaseId);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentSystemFailure", result.Error!.Code);

        using var read = DatabasesTestContext.Create(FakeCurrentUser.Admin(), world.Store);
        Assert.Equal(2, await read.Databases.CountAsync());
    }

    /// <summary>A successful drop removes the row as well as the database.</summary>
    [Fact]
    public async Task A_successful_drop_removes_the_row_as_well_as_the_database()
    {
        var world = await WorldAsync();

        await world.DropAsync(world.OwnDatabaseId);

        using var read = DatabasesTestContext.Create(FakeCurrentUser.Admin(), world.Store);
        Assert.Equal([world.StrangerDatabaseId], await read.Databases.Select(row => row.Id).ToListAsync());
    }

    /// <summary>A drop is journalled with the database name.</summary>
    [Fact]
    public async Task A_drop_is_journalled_with_the_database_name()
    {
        var world = await WorldAsync();

        await world.DropAsync(world.OwnDatabaseId);

        var entry = Assert.Single(world.Audit.Entries);
        Assert.Equal(AuditActions.DatabaseDropped, entry.Action);
        Assert.Equal("shop", entry.Subject);
        Assert.True(entry.Succeeded);
    }

    /// <summary>A refused drop is journalled as a failure naming what was probed for.</summary>
    [Fact]
    public async Task A_refused_drop_is_journalled_as_a_failure_naming_what_was_probed_for()
    {
        var world = await WorldAsync();

        await world.DropAsync(world.StrangerDatabaseId);

        var entry = Assert.Single(world.Audit.Entries);
        Assert.Equal(AuditActions.DatabaseDropped, entry.Action);
        Assert.False(entry.Succeeded);
        Assert.Equal(world.StrangerDatabaseId.ToString(), entry.Subject);
    }

    /// <summary>Builds a world holding one database for each of two accounts.</summary>
    private static async Task<World> WorldAsync()
    {
        var store = Guid.NewGuid().ToString();

        using var seed = DatabasesTestContext.Create(FakeCurrentUser.Admin(), store);
        var own = DatabasesTestContext.Row(OwnerAccountId, "alice", "shop", "shopuser");
        var stranger = DatabasesTestContext.Row(StrangerAccountId, "bob", "ledger", "ledgeruser");
        seed.Databases.AddRange(own, stranger);
        await seed.SaveChangesAsync();

        return new World(store, own.Id, stranger.Id);
    }

    /// <summary>Everything one drop test needs, wired the way the Host wires it.</summary>
    private sealed class World
    {
        /// <summary>The shared in-memory store both principals read.</summary>
        public string Store { get; }

        /// <summary>The signed-in customer's own database.</summary>
        public Guid OwnDatabaseId { get; }

        /// <summary>The other tenant's database, which must answer 404.</summary>
        public Guid StrangerDatabaseId { get; }

        /// <summary>The agent double every call is recorded on.</summary>
        public RecordingAgentDbClient Agent { get; } = new();

        /// <summary>The journal every entry lands in.</summary>
        public RecordingAuditWriter Audit { get; } = new();

        /// <summary>The handler under test.</summary>
        public DropDatabaseCommandHandler Handler { get; }

        /// <summary>Wires one world.</summary>
        /// <param name="store">The shared in-memory store.</param>
        /// <param name="ownDatabaseId">The signed-in customer's database.</param>
        /// <param name="strangerDatabaseId">The other tenant's database.</param>
        public World(string store, Guid ownDatabaseId, Guid strangerDatabaseId)
        {
            Store = store;
            OwnDatabaseId = ownDatabaseId;
            StrangerDatabaseId = strangerDatabaseId;

            var currentUser = FakeCurrentUser.Customer(OwnerAccountId);
            Handler = new DropDatabaseCommandHandler(
                DatabasesTestContext.Create(currentUser, store),
                new StubAccountDirectory(new AccountSnapshot(OwnerAccountId, "alice", 5, 5, 5, 5, 5, 1_024)),
                Agent,
                new DatabaseAuditJournal(Audit, currentUser));
        }

        /// <summary>Runs the handler with the usual audit context.</summary>
        /// <param name="databaseId">Which database to drop.</param>
        public async Task<Result<bool>> DropAsync(Guid databaseId)
        {
            return await Handler.HandleAsync(
                new DropDatabaseCommand(databaseId, "203.0.113.7", "tests"), CancellationToken.None);
        }
    }
}
