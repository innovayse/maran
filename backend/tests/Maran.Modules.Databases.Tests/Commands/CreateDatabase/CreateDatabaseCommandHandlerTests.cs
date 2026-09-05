using Maran.Modules.Databases.Commands.CreateDatabase;
using Maran.Modules.Databases.Common;
using Maran.Modules.Databases.Persistence;
using Maran.Modules.Databases.Services;
using Maran.Modules.Databases.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;
using Maran.SharedKernel.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using AgentCreatedDatabaseDto = Maran.Agent.Client.Services.DbService.CreatedDatabaseDto;

namespace Maran.Modules.Databases.Tests.Commands.CreateDatabase;

/// <summary>
/// What creating a database refuses, in what order it touches the two stores, and what it leaves
/// behind when the second one fails.
/// </summary>
public sealed class CreateDatabaseCommandHandlerTests
{
    private static readonly Guid AccountId = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");
    private static readonly Guid StrangerAccountId = Guid.Parse("bbbbbbbb-0000-4000-8000-000000000002");
    private static readonly DateTimeOffset Now = new(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);

    /// <summary>A database beyond the plans allowance is refused before the agent is called at all.</summary>
    [Fact]
    public async Task A_database_beyond_the_plans_allowance_is_refused_before_the_agent_is_called_at_all()
    {
        var world = new World(maxDatabases: 1);
        world.Context.Databases.Add(DatabasesTestContext.Row(AccountId, "alice", "first"));
        await world.Context.SaveChangesAsync();

        var result = await world.HandleAsync("second", "seconduser");

        Assert.False(result.IsSuccess);
        Assert.Equal("DatabaseLimitReached", result.Error!.Code);
        Assert.Empty(world.Agent.Creates);
    }

    /// <summary>A name another tenant already uses is still available because names are prefixed.</summary>
    [Fact]
    public async Task A_name_another_tenant_already_uses_is_still_available_because_names_are_prefixed()
    {
        // alice_shop and bob_shop are two different MySQL databases. This is the test that says the
        // prefix works end to end: `shop` being taken for bob must not make it taken for alice, or
        // the first tenant to ask occupies a name every other tenant can then never use.
        var shared = Guid.NewGuid().ToString();
        var admin = DatabasesTestContext.Create(FakeCurrentUser.Admin(), shared);
        admin.Databases.Add(DatabasesTestContext.Row(StrangerAccountId, "bob", "shop"));
        await admin.SaveChangesAsync();

        var world = new World(databaseName: shared);

        var result = await world.HandleAsync("shop", "shopuser");

        Assert.True(result.IsSuccess);
        Assert.Equal("alice_shop", result.Value.FullName);

        var rows = await admin.Databases.IgnoreQueryFilters().Select(row => row.FullName).ToListAsync();
        Assert.Contains("alice_shop", rows);
        Assert.Contains("bob_shop", rows);
    }

    /// <summary>A name this tenant already uses is refused as taken.</summary>
    [Fact]
    public async Task A_name_this_tenant_already_uses_is_refused_as_taken()
    {
        // The other half of the prefix test: proving `shop` is free across tenants is only half an
        // answer if it is also free within one, which would give the account two rows for one
        // database.
        var world = new World();
        world.Context.Databases.Add(DatabasesTestContext.Row(AccountId, "alice", "shop"));
        await world.Context.SaveChangesAsync();

        var result = await world.HandleAsync("shop", "another");

        Assert.False(result.IsSuccess);
        Assert.Equal("DatabaseNameTaken", result.Error!.Code);
        Assert.Empty(world.Agent.Creates);
    }

    /// <summary>The agent is addressed by suffix and by the account never by a fully qualified name.</summary>
    [Fact]
    public async Task The_agent_is_addressed_by_suffix_and_by_the_account_never_by_a_fully_qualified_name()
    {
        // A fully-qualified name off the panel would be a request that CAN express another tenant's
        // database. The agent applies the prefix itself precisely so the question is unaskable.
        var world = new World();

        await world.HandleAsync("shop", "shopuser");

        var call = Assert.Single(world.Agent.Creates);
        Assert.Equal("alice", call.AccountUsername);
        Assert.Equal("shop", call.DatabaseName);
        Assert.Equal("shopuser", call.DbUsername);
    }

    /// <summary>The recorded names are the ones the agent reported and not names the panel rebuilt.</summary>
    [Fact]
    public async Task The_recorded_names_are_the_ones_the_agent_reported_and_not_names_the_panel_rebuilt()
    {
        // The agent answers with a separator the panel does not choose. Recording its answer is what
        // stops a later drop addressing a name the server never had.
        var world = new World();
        world.Agent.CreateResult = Result<AgentCreatedDatabaseDto>.Ok(
            new AgentCreatedDatabaseDto("srv-alice+shop", "srv-alice+shopuser"));

        var result = await world.HandleAsync("shop", "shopuser");

        Assert.True(result.IsSuccess);
        Assert.Equal("srv-alice+shop", result.Value.FullName);
        Assert.Equal("srv-alice+shopuser", result.Value.DbUserName);
    }

    /// <summary>A refused provisioning leaves no database row behind.</summary>
    [Fact]
    public async Task A_refused_provisioning_leaves_no_database_row_behind()
    {
        var world = new World();
        world.Agent.CreateResult = Result<AgentCreatedDatabaseDto>.Fail(
            Error.Of("AgentSystemFailure", ErrorType.Failure));

        var result = await world.HandleAsync("shop", "shopuser");

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentSystemFailure", result.Error!.Code);
        Assert.Empty(await world.Context.Databases.IgnoreQueryFilters().ToListAsync());
    }

    /// <summary>A row insert that fails after the agent created the database leaves no orphan.</summary>
    [Fact]
    public async Task A_row_insert_that_fails_after_the_agent_created_the_database_leaves_no_orphan()
    {
        // The failure this module's compensation exists for. The password is shown once and stored
        // nowhere, so a live database with no row is unreachable forever: the obvious retry hits the
        // agent's AlreadyExists, which deliberately does NOT reset the password. The database must
        // therefore be dropped again, so a retry starts clean.
        var world = new World(saveFailure: new DbUpdateException("the connection dropped", new IOException()));

        var result = await world.HandleAsync("shop", "shopuser");

        Assert.False(result.IsSuccess);
        Assert.Equal("DatabaseProvisioningFailed", result.Error!.Code);
        Assert.Empty(await world.Context.Databases.IgnoreQueryFilters().ToListAsync());

        var compensation = Assert.Single(world.Agent.Drops);
        Assert.Equal("alice", compensation.AccountUsername);
        Assert.Equal("shop", compensation.DatabaseName);
        Assert.Equal("shopuser", compensation.DbUsername);
    }

    /// <summary>A database failure that is not a duplicate is not reported as a name already taken.</summary>
    [Fact]
    public async Task A_database_failure_that_is_not_a_duplicate_is_not_reported_as_a_name_already_taken()
    {
        // Reporting a dropped connection as "that name is in use" is the message that discourages
        // the retry which would actually repair the customer's database.
        var world = new World(saveFailure: new DbUpdateException("the connection dropped", new IOException()));

        var result = await world.HandleAsync("shop", "shopuser");

        Assert.False(result.IsSuccess);
        Assert.NotEqual("DatabaseNameTaken", result.Error!.Code);
        Assert.NotEqual("DatabaseUserNameTaken", result.Error.Code);
        Assert.Equal("DatabaseProvisioningFailed", result.Error.Code);
    }

    /// <summary>A postgres failure that is not a unique violation is compensated and not reported as taken.</summary>
    [Fact]
    public async Task A_postgres_failure_that_is_not_a_unique_violation_is_compensated_and_not_reported_as_taken()
    {
        // The narrowing, asserted at its edge rather than only at a distance. The inner exception IS
        // a PostgresException here, so only the SqlState tells the two apart — which is precisely the
        // discrimination the previous plan's Ssl module did not make when it caught DbUpdateException
        // wholesale and told every customer their name was already taken.
        var world = new World(saveFailure: new DbUpdateException(
            "the connection failed",
            new PostgresException(
                messageText: "connection failure",
                severity: "FATAL",
                invariantSeverity: "FATAL",
                sqlState: PostgresErrorCodes.ConnectionFailure)));

        var result = await world.HandleAsync("shop", "shopuser");

        Assert.Equal("DatabaseProvisioningFailed", result.Error!.Code);
        Assert.Single(world.Agent.Drops);
    }

    /// <summary>A concurrent creation of the same name is reported as taken and is never compensated.</summary>
    [Fact]
    public async Task A_concurrent_creation_of_the_same_name_is_reported_as_taken_and_is_never_compensated()
    {
        // The one post-create failure that must NOT drop: the winner's row owns the database now on
        // the server, so compensating would delete their data.
        var world = new World(saveFailure: UniqueViolation("IX_Databases_FullName"));

        var result = await world.HandleAsync("shop", "shopuser");

        Assert.False(result.IsSuccess);
        Assert.Equal("DatabaseNameTaken", result.Error!.Code);
        Assert.Empty(world.Agent.Drops);
    }

    /// <summary>A duplicate dedicated user is compensated because no row owns the new database.</summary>
    [Fact]
    public async Task A_duplicate_dedicated_user_is_compensated_because_no_row_owns_the_new_database()
    {
        // The other 23505: the conflict is on the USER, so nothing owns the database this request
        // just made and it is an orphan like any other.
        var world = new World(saveFailure: UniqueViolation("IX_Databases_DbUserName"));

        var result = await world.HandleAsync("shop", "shopuser");

        Assert.False(result.IsSuccess);
        Assert.Equal("DatabaseUserNameTaken", result.Error!.Code);
        Assert.Single(world.Agent.Drops);
    }

    /// <summary>The generated password is returned once and is absent from every later read.</summary>
    [Fact]
    public async Task The_generated_password_is_returned_once_and_is_absent_from_every_later_read()
    {
        var world = new World();

        var result = await world.HandleAsync("shop", "shopuser");

        Assert.True(result.IsSuccess);
        Assert.NotEmpty(result.Value.Password.Reveal());

        // The value the agent was handed is the value the customer is shown; a mismatch would be a
        // password the customer cannot use.
        Assert.Equal(Assert.Single(world.Agent.Creates).Password.Reveal(), result.Value.Password.Reveal());

        // And nothing in the row holds it. Asserted over every mapped property rather than over a
        // remembered list, so a password column added later fails HERE.
        var row = await world.Context.Databases.IgnoreQueryFilters().SingleAsync();
        var entry = world.Context.Entry(row);
        foreach (var property in entry.Properties)
        {
            Assert.NotEqual(result.Value.Password.Reveal(), property.CurrentValue as string);
        }
    }

    /// <summary>The generated password is drawn only from the alphabet the agent accepts.</summary>
    [Fact]
    public async Task The_generated_password_is_drawn_only_from_the_alphabet_the_agent_accepts()
    {
        // A password outside it is a creation the agent refuses AFTER the customer has been promised
        // a database.
        var world = new World();

        var result = await world.HandleAsync("shop", "shopuser");

        Assert.All(
            result.Value.Password.Reveal(),
            character =>
            {
                Assert.True(
                    ProvisionedPasswordGenerator.Alphabet.Contains(character, StringComparison.Ordinal),
                    $"the generator produced {character}, which the agent's Password type would refuse");
            });
    }

    /// <summary>A creation is journalled with the database name and never with the password.</summary>
    [Fact]
    public async Task A_creation_is_journalled_with_the_database_name_and_never_with_the_password()
    {
        var world = new World();

        var result = await world.HandleAsync("shop", "shopuser");

        var entry = Assert.Single(world.Audit.Entries);
        Assert.Equal(AuditActions.DatabaseCreated, entry.Action);
        Assert.Equal("shop", entry.Subject);
        Assert.True(entry.Succeeded);
        Assert.DoesNotContain(result.Value.Password.Reveal(), entry.Subject, StringComparison.Ordinal);
    }

    /// <summary>A refused creation is journalled as a failure.</summary>
    [Fact]
    public async Task A_refused_creation_is_journalled_as_a_failure()
    {
        var world = new World(maxDatabases: 0);

        await world.HandleAsync("shop", "shopuser");

        var entry = Assert.Single(world.Audit.Entries);
        Assert.Equal(AuditActions.DatabaseCreated, entry.Action);
        Assert.False(entry.Succeeded);
    }

    /// <summary>An account the caller may not see is answered not found before the agent is called.</summary>
    [Fact]
    public async Task An_account_the_caller_may_not_see_is_answered_not_found_before_the_agent_is_called()
    {
        var world = new World();

        var result = await world.HandleAsync("shop", "shopuser", accountId: StrangerAccountId);

        Assert.False(result.IsSuccess);
        Assert.Equal("AccountNotFound", result.Error!.Code);
        Assert.Empty(world.Agent.Creates);
    }

    /// <summary>A name too long once the account prefix is added is refused with its own code.</summary>
    [Fact]
    public async Task A_name_too_long_once_the_account_prefix_is_added_is_refused_with_its_own_code()
    {
        // MySQL's ceiling is on the PREFIXED name, which only this layer can measure — it is the only
        // one holding both the suffix and the account's user name.
        var world = new World(username: new string('a', 40));

        var result = await world.HandleAsync(new string('b', 25), "u");

        Assert.False(result.IsSuccess);
        Assert.Equal("DatabaseNameTooLong", result.Error!.Code);
        Assert.Empty(world.Agent.Creates);
    }

    /// <summary>A user name too long once the account prefix is added is refused with its own code.</summary>
    [Fact]
    public async Task A_user_name_too_long_once_the_account_prefix_is_added_is_refused_with_its_own_code()
    {
        // The user-name ceiling is the tighter one, and getting it wrong is worse: older servers
        // TRUNCATE rather than refuse, and a truncated name is two accounts sharing one login.
        var world = new World(username: new string('a', 25));

        var result = await world.HandleAsync("shop", new string('b', 10));

        Assert.False(result.IsSuccess);
        Assert.Equal("DatabaseUserNameTooLong", result.Error!.Code);
        Assert.Empty(world.Agent.Creates);
    }

    /// <summary>Builds the unique violation PostgreSQL raises, naming the index that fired.</summary>
    /// <param name="constraintName">The index whose uniqueness was violated.</param>
    private static DbUpdateException UniqueViolation(string constraintName)
    {
        return new DbUpdateException(
            "duplicate key value violates unique constraint",
            new PostgresException(
                messageText: "duplicate key value violates unique constraint",
                severity: "ERROR",
                invariantSeverity: "ERROR",
                sqlState: PostgresErrorCodes.UniqueViolation,
                constraintName: constraintName));
    }

    /// <summary>Everything one creation test needs, wired the way the Host wires it.</summary>
    private sealed class World
    {
        /// <summary>The module's context, seen as the customer who owns <see cref="AccountId"/>.</summary>
        public DatabasesDbContext Context { get; }

        /// <summary>The agent double every call is recorded on.</summary>
        public RecordingAgentDbClient Agent { get; } = new();

        /// <summary>The journal every entry lands in.</summary>
        public RecordingAuditWriter Audit { get; } = new();

        /// <summary>The handler under test.</summary>
        public CreateDatabaseCommandHandler Handler { get; }

        /// <summary>Wires one world.</summary>
        /// <param name="maxDatabases">The plan allowance the stubbed account carries.</param>
        /// <param name="databaseName">A shared in-memory database, when the test needs two contexts.</param>
        /// <param name="saveFailure">What the row insert throws, when the test kills it.</param>
        /// <param name="username">The account's system user name, which forms the prefix.</param>
        public World(
            int maxDatabases = 5,
            string? databaseName = null,
            Exception? saveFailure = null,
            string username = "alice")
        {
            var currentUser = FakeCurrentUser.Customer(AccountId);
            Context = DatabasesTestContext.Create(currentUser, databaseName, saveFailure);
            Handler = new CreateDatabaseCommandHandler(
                Context,
                new StubAccountDirectory(new AccountSnapshot(AccountId, username, 5, maxDatabases, 5, 5, 5, 1_024)),
                Agent,
                new DatabaseAuditJournal(Audit, currentUser),
                new FakeClock(Now),
                NullLogger<CreateDatabaseCommandHandler>.Instance);
        }

        /// <summary>Runs the handler with the usual audit context.</summary>
        /// <param name="name">The database name suffix.</param>
        /// <param name="dbUserName">The user name suffix.</param>
        /// <param name="accountId">The account to create under; the owner by default.</param>
        public async Task<Result<CreatedDatabaseDto>> HandleAsync(
            string name,
            string dbUserName,
            Guid? accountId = null)
        {
            return await Handler.HandleAsync(
                new CreateDatabaseCommand(accountId ?? AccountId, name, dbUserName, "203.0.113.7", "tests"),
                CancellationToken.None);
        }
    }
}
