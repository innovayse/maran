using Maran.Modules.Sftp.Commands.CreateSftpUser;
using Maran.Modules.Sftp.Common;
using Maran.Modules.Sftp.Persistence;
using Maran.Modules.Sftp.Services;
using Maran.Modules.Sftp.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;
using Maran.SharedKernel.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace Maran.Modules.Sftp.Tests.Commands.CreateSftpUser;

/// <summary>
/// What creating an SFTP login refuses, in what order it touches the two stores, and what it leaves
/// behind when the second one fails.
/// </summary>
public sealed class CreateSftpUserCommandHandlerTests
{
    private static readonly Guid AccountId = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");
    private static readonly Guid StrangerAccountId = Guid.Parse("bbbbbbbb-0000-4000-8000-000000000002");
    private static readonly DateTimeOffset Now = new(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);

    /// <summary>An sftp user beyond the plans allowance is refused before the agent is called.</summary>
    [Fact]
    public async Task An_sftp_user_beyond_the_plans_allowance_is_refused_before_the_agent_is_called()
    {
        var world = new World(maxSftpUsers: 1);
        world.Context.SftpUsers.Add(SftpTestContext.Row(AccountId, "alice", "first"));
        await world.Context.SaveChangesAsync();

        var result = await world.HandleAsync("second");

        Assert.False(result.IsSuccess);
        Assert.Equal("SftpUserLimitReached", result.Error!.Code);
        Assert.Empty(world.Agent.Creates);
    }

    /// <summary>A name another tenant uses is still available because names are prefixed.</summary>
    [Fact]
    public async Task A_name_another_tenant_uses_is_still_available_because_names_are_prefixed()
    {
        // alice_deploy and bob_deploy are two different system logins. This is the test that says the
        // prefix works end to end: `deploy` being taken for bob must not make it taken for alice, or
        // the first tenant to ask occupies a name every other tenant can then never use.
        var shared = Guid.NewGuid().ToString();
        var admin = SftpTestContext.Create(FakeCurrentUser.Admin(), shared);
        admin.SftpUsers.Add(SftpTestContext.Row(StrangerAccountId, "bob", "deploy"));
        await admin.SaveChangesAsync();

        var world = new World(databaseName: shared);

        var result = await world.HandleAsync("deploy");

        Assert.True(result.IsSuccess);
        Assert.Equal("alice_deploy", result.Value.FullName);

        var rows = await admin.SftpUsers.IgnoreQueryFilters().Select(row => row.FullName).ToListAsync();
        Assert.Contains("alice_deploy", rows);
        Assert.Contains("bob_deploy", rows);
    }

    /// <summary>A name this tenant already uses is refused as taken.</summary>
    [Fact]
    public async Task A_name_this_tenant_already_uses_is_refused_as_taken()
    {
        // The other half of the prefix test: proving `deploy` is free across tenants is only half an
        // answer if it is also free within one, which would give the account two rows for one login.
        var world = new World();
        world.Context.SftpUsers.Add(SftpTestContext.Row(AccountId, "alice", "deploy"));
        await world.Context.SaveChangesAsync();

        var result = await world.HandleAsync("deploy");

        Assert.False(result.IsSuccess);
        Assert.Equal("SftpUserNameTaken", result.Error!.Code);
        Assert.Empty(world.Agent.Creates);
    }

    /// <summary>The agent is addressed by suffix and by the account never by a fully qualified name.</summary>
    [Fact]
    public async Task The_agent_is_addressed_by_suffix_and_by_the_account_never_by_a_fully_qualified_name()
    {
        // A fully-qualified login off the panel would be a request that CAN express another tenant's
        // login. The agent applies the prefix itself precisely so the question is unaskable.
        var world = new World();

        await world.HandleAsync("deploy");

        var call = Assert.Single(world.Agent.Creates);
        Assert.Equal("alice", call.AccountUsername);
        Assert.Equal("deploy", call.SftpUsername);
    }

    /// <summary>The recorded login is the one the agent reported and not a name the panel rebuilt.</summary>
    [Fact]
    public async Task The_recorded_login_is_the_one_the_agent_reported_and_not_a_name_the_panel_rebuilt()
    {
        // The agent answers with a separator the panel does not choose. Recording its answer is what
        // stops the panel showing a customer a user name their SFTP client is refused with.
        var world = new World();
        world.Agent.CreateResult = Result<string>.Ok("srv-alice+deploy");

        var result = await world.HandleAsync("deploy");

        Assert.True(result.IsSuccess);
        Assert.Equal("srv-alice+deploy", result.Value.FullName);
    }

    /// <summary>A refused provisioning leaves no sftp user row behind.</summary>
    [Fact]
    public async Task A_refused_provisioning_leaves_no_sftp_user_row_behind()
    {
        var world = new World();
        world.Agent.CreateResult = Result<string>.Fail(Error.Of("AgentSystemFailure", ErrorType.Failure));

        var result = await world.HandleAsync("deploy");

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentSystemFailure", result.Error!.Code);
        Assert.Empty(await world.Context.SftpUsers.IgnoreQueryFilters().ToListAsync());
    }

    /// <summary>A row insert that fails after the agent created the login leaves no orphan.</summary>
    [Fact]
    public async Task A_row_insert_that_fails_after_the_agent_created_the_login_leaves_no_orphan()
    {
        // The failure this module's compensation exists for. The password is shown once and stored
        // nowhere, so a live login with no row is unusable forever AND still a key into the account's
        // home: the obvious retry hits the agent's AlreadyExists, which deliberately does NOT reset
        // the password. The login must therefore be deleted again, so a retry starts clean.
        var world = new World(saveFailure: new DbUpdateException("the connection dropped", new IOException()));

        var result = await world.HandleAsync("deploy");

        Assert.False(result.IsSuccess);
        Assert.Equal("SftpUserProvisioningFailed", result.Error!.Code);
        Assert.Empty(await world.Context.SftpUsers.IgnoreQueryFilters().ToListAsync());

        var compensation = Assert.Single(world.Agent.Deletes);
        Assert.Equal("alice", compensation.AccountUsername);
        Assert.Equal("deploy", compensation.SftpUsername);
    }

    /// <summary>A database failure that is not a duplicate is not reported as a name already taken.</summary>
    [Fact]
    public async Task A_database_failure_that_is_not_a_duplicate_is_not_reported_as_a_name_already_taken()
    {
        // Reporting a dropped connection as "that name is in use" is the message that discourages
        // the retry which would actually repair the customer's login.
        var world = new World(saveFailure: new DbUpdateException("the connection dropped", new IOException()));

        var result = await world.HandleAsync("deploy");

        Assert.False(result.IsSuccess);
        Assert.NotEqual("SftpUserNameTaken", result.Error!.Code);
        Assert.Equal("SftpUserProvisioningFailed", result.Error.Code);
    }

    /// <summary>A postgres failure that is not a unique violation is compensated and not reported as taken.</summary>
    [Fact]
    public async Task A_postgres_failure_that_is_not_a_unique_violation_is_compensated_and_not_reported_as_taken()
    {
        // The narrowing, asserted at its edge rather than only at a distance. The inner exception IS
        // a PostgresException here, so ONLY the SqlState tells the two apart — which is precisely
        // the discrimination a handler that caught DbUpdateException wholesale would not make.
        var world = new World(saveFailure: new DbUpdateException(
            "the connection failed",
            new PostgresException(
                messageText: "connection failure",
                severity: "FATAL",
                invariantSeverity: "FATAL",
                sqlState: PostgresErrorCodes.ConnectionFailure)));

        var result = await world.HandleAsync("deploy");

        Assert.Equal("SftpUserProvisioningFailed", result.Error!.Code);
        Assert.Single(world.Agent.Deletes);
    }

    /// <summary>A concurrent creation of the same name is reported as taken and is never compensated.</summary>
    [Theory]
    [InlineData("IX_SftpUsers_FullName")]
    [InlineData("IX_SftpUsers_AccountId_Name")]
    public async Task A_concurrent_creation_of_the_same_name_is_reported_as_taken_and_is_never_compensated(
        string constraintName)
    {
        // The one post-create failure that must NOT delete: the winner's row owns the login now on
        // the host, and deleting it would revoke a credential that customer has already been shown
        // and — since nothing keeps a copy — cannot be shown again.
        //
        // Both unique indexes are exercised because both mean the same thing here: a login is one
        // name, not a pair, so unlike the Databases module there is no second narrowing by WHICH
        // index fired, and this theory is what says so.
        var world = new World(saveFailure: UniqueViolation(constraintName));

        var result = await world.HandleAsync("deploy");

        Assert.False(result.IsSuccess);
        Assert.Equal("SftpUserNameTaken", result.Error!.Code);
        Assert.Empty(world.Agent.Deletes);
    }

    /// <summary>The generated password is returned once and is absent from every later read.</summary>
    [Fact]
    public async Task The_generated_password_is_returned_once_and_is_absent_from_every_later_read()
    {
        var world = new World();

        var result = await world.HandleAsync("deploy");

        Assert.True(result.IsSuccess);
        Assert.NotEmpty(result.Value.Password.Reveal());

        // The value the agent was handed is the value the customer is shown; a mismatch would be a
        // password the customer cannot sign in with.
        Assert.Equal(Assert.Single(world.Agent.Creates).Password.Reveal(), result.Value.Password.Reveal());

        // And nothing in the row holds it. Asserted over every mapped property rather than over a
        // remembered list, so a password column added later fails HERE.
        var row = await world.Context.SftpUsers.IgnoreQueryFilters().SingleAsync();
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
        // a login — and the colon and newline it excludes are what stop a value breaking out of the
        // `user:password` line chpasswd reads.
        var world = new World();

        var result = await world.HandleAsync("deploy");

        Assert.All(
            result.Value.Password.Reveal(),
            character =>
            {
                Assert.True(
                    ProvisionedPasswordGenerator.Alphabet.Contains(character, StringComparison.Ordinal),
                    $"the generator produced {character}, which the agent's Password type would refuse");
            });
    }

    /// <summary>A creation is journalled with the login name and never with the password.</summary>
    [Fact]
    public async Task A_creation_is_journalled_with_the_login_name_and_never_with_the_password()
    {
        var world = new World();

        var result = await world.HandleAsync("deploy");

        var entry = Assert.Single(world.Audit.Entries);
        Assert.Equal(AuditActions.SftpUserCreated, entry.Action);
        Assert.Equal("deploy", entry.Subject);
        Assert.True(entry.Succeeded);
        Assert.DoesNotContain(result.Value.Password.Reveal(), entry.Subject, StringComparison.Ordinal);
    }

    /// <summary>A refused creation is journalled as a failure.</summary>
    [Fact]
    public async Task A_refused_creation_is_journalled_as_a_failure()
    {
        var world = new World(maxSftpUsers: 0);

        await world.HandleAsync("deploy");

        var entry = Assert.Single(world.Audit.Entries);
        Assert.Equal(AuditActions.SftpUserCreated, entry.Action);
        Assert.False(entry.Succeeded);
    }

    /// <summary>An account the caller may not see is answered not found before the agent is called.</summary>
    [Fact]
    public async Task An_account_the_caller_may_not_see_is_answered_not_found_before_the_agent_is_called()
    {
        var world = new World();

        var result = await world.HandleAsync("deploy", accountId: StrangerAccountId);

        Assert.False(result.IsSuccess);
        Assert.Equal("AccountNotFound", result.Error!.Code);
        Assert.Empty(world.Agent.Creates);
    }

    /// <summary>A name too long once the account prefix is added is refused with its own code.</summary>
    [Fact]
    public async Task A_name_too_long_once_the_account_prefix_is_added_is_refused_with_its_own_code()
    {
        // useradd's ceiling is on the PREFIXED login, which only this layer can measure — it is the
        // only one holding both the suffix and the account's user name. And getting it wrong is not
        // cosmetic: an over-long name is refused by the agent after the customer has been promised
        // a login.
        var world = new World(username: new string('a', 25));

        var result = await world.HandleAsync(new string('b', 10));

        Assert.False(result.IsSuccess);
        Assert.Equal("SftpUserNameTooLong", result.Error!.Code);
        Assert.Empty(world.Agent.Creates);
    }

    /// <summary>A name that exactly fills the useradd ceiling is accepted.</summary>
    [Fact]
    public async Task A_name_that_exactly_fills_the_useradd_ceiling_is_accepted()
    {
        // The boundary from the other side, so the length check cannot pass by being off by one in
        // the direction that refuses names the host would have taken.
        var world = new World(username: new string('a', 25));

        var result = await world.HandleAsync(new string('b', 6));

        Assert.True(result.IsSuccess);
        Assert.Single(world.Agent.Creates);
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
        public SftpDbContext Context { get; }

        /// <summary>The agent double every call is recorded on.</summary>
        public RecordingAgentSftpClient Agent { get; } = new();

        /// <summary>The journal every entry lands in.</summary>
        public RecordingAuditWriter Audit { get; } = new();

        /// <summary>The handler under test.</summary>
        public CreateSftpUserCommandHandler Handler { get; }

        /// <summary>Wires one world.</summary>
        /// <param name="maxSftpUsers">The plan allowance the stubbed account carries.</param>
        /// <param name="databaseName">A shared in-memory database, when the test needs two contexts.</param>
        /// <param name="saveFailure">What the row insert throws, when the test kills it.</param>
        /// <param name="username">The account's system user name, which forms the prefix.</param>
        public World(
            int maxSftpUsers = 5,
            string? databaseName = null,
            Exception? saveFailure = null,
            string username = "alice")
        {
            var currentUser = FakeCurrentUser.Customer(AccountId);
            Context = SftpTestContext.Create(currentUser, databaseName, saveFailure);
            Handler = new CreateSftpUserCommandHandler(
                Context,
                new StubAccountDirectory(new AccountSnapshot(AccountId, username, 5, 5, maxSftpUsers, 5, 5, 1_024)),
                Agent,
                new SftpAuditJournal(Audit, currentUser),
                new FakeClock(Now),
                NullLogger<CreateSftpUserCommandHandler>.Instance);
        }

        /// <summary>Runs the handler with the usual audit context.</summary>
        /// <param name="name">The login name suffix.</param>
        /// <param name="accountId">The account to create under; the owner by default.</param>
        public async Task<Result<CreatedSftpUserDto>> HandleAsync(string name, Guid? accountId = null)
        {
            return await Handler.HandleAsync(
                new CreateSftpUserCommand(accountId ?? AccountId, name, "203.0.113.7", "tests"),
                CancellationToken.None);
        }
    }
}
