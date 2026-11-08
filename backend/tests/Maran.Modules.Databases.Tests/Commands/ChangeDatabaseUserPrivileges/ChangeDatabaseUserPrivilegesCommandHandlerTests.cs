using Maran.Agent.Client.Services.DbService;
using Maran.Modules.Databases.Commands.ChangeDatabaseUserPrivileges;
using Maran.Modules.Databases.Domain.Enums;
using Maran.Modules.Databases.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Maran.Modules.Databases.Tests.Commands.ChangeDatabaseUserPrivileges;

/// <summary>What changing a users privileges on a database asks of the agent, records, refuses and compensates.</summary>
public sealed class ChangeDatabaseUserPrivilegesCommandHandlerTests
{
    /// <summary>Granting a user that holds nothing sends the set to the agent and records one grant.</summary>
    [Fact]
    public async Task Granting_a_user_that_holds_nothing_sends_the_set_to_the_agent_and_records_one_grant()
    {
        var world = await UsersTestWorld.CreateAsync();

        var result = await ChangeAsync(
            world, world.ReaderUserId, world.OwnDatabaseId, DatabasePrivilege.Select, DatabasePrivilege.ShowView);

        Assert.True(result.IsSuccess);
        var call = Assert.Single(world.Agent.PrivilegeChanges);
        Assert.Equal("alice", call.AccountUsername);
        Assert.Equal("reader", call.DbUsername);
        Assert.Equal("shop", call.DatabaseName);
        Assert.Equal([DatabasePrivilegeKind.Select, DatabasePrivilegeKind.ShowView], call.Privileges);

        var reader = (await world.AllUsersAsync()).Single(user => { return user.Id == world.ReaderUserId; });
        var grant = Assert.Single(reader.Grants);
        Assert.Equal(world.OwnDatabaseId, grant.DatabaseId);
        Assert.Equal(DatabasePrivilegeSet.Select | DatabasePrivilegeSet.ShowView, grant.Privileges);
    }

    /// <summary>Changing an existing set replaces it exactly and does not add a second grant.</summary>
    [Fact]
    public async Task Changing_an_existing_set_replaces_it_exactly_and_does_not_add_a_second_grant()
    {
        var world = await UsersTestWorld.CreateAsync();

        var result = await ChangeAsync(world, world.DedicatedUserId, world.OwnDatabaseId, DatabasePrivilege.Delete);

        Assert.True(result.IsSuccess);
        var dedicated = (await world.AllUsersAsync()).Single(user => { return user.Id == world.DedicatedUserId; });
        Assert.Equal(DatabasePrivilegeSet.Delete, Assert.Single(dedicated.Grants).Privileges);
    }

    /// <summary>An empty set removes the grant and still tells the agent to revoke everything.</summary>
    [Fact]
    public async Task An_empty_set_removes_the_grant_and_still_tells_the_agent_to_revoke_everything()
    {
        var world = await UsersTestWorld.CreateAsync();

        var result = await ChangeAsync(world, world.DedicatedUserId, world.OwnDatabaseId);

        Assert.True(result.IsSuccess);
        Assert.Empty(Assert.Single(world.Agent.PrivilegeChanges).Privileges);
        var dedicated = (await world.AllUsersAsync()).Single(user => { return user.Id == world.DedicatedUserId; });
        Assert.Empty(dedicated.Grants);
    }

    /// <summary>Another tenants user answers not found and the agent is never called.</summary>
    [Fact]
    public async Task Another_tenants_user_answers_not_found_and_the_agent_is_never_called()
    {
        var world = await UsersTestWorld.CreateAsync();

        var result = await ChangeAsync(world, world.StrangerUserId, world.OwnDatabaseId, DatabasePrivilege.Select);

        Assert.Equal("DatabaseUserNotFound", result.Error!.Code);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        Assert.Empty(world.Agent.PrivilegeChanges);
    }

    /// <summary>Another tenants database answers not found and nothing is granted across accounts.</summary>
    [Fact]
    public async Task Another_tenants_database_answers_not_found_and_nothing_is_granted_across_accounts()
    {
        var world = await UsersTestWorld.CreateAsync();

        var result = await ChangeAsync(world, world.ReaderUserId, world.StrangerDatabaseId, DatabasePrivilege.Select);

        Assert.Equal("DatabaseNotFound", result.Error!.Code);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        Assert.Empty(world.Agent.PrivilegeChanges);
    }

    /// <summary>An administrator is refused a grant across two accounts all the same.</summary>
    [Fact]
    public async Task An_administrator_is_refused_a_grant_across_two_accounts_all_the_same()
    {
        // The administrator's context shows every tenant, so only the handler's own same-account check
        // stands between a mistaken request and a cross-tenant grant.
        var world = await UsersTestWorld.CreateAsync(asAdministrator: true);

        var result = await ChangeAsync(world, world.ReaderUserId, world.StrangerDatabaseId, DatabasePrivilege.Select);

        Assert.Equal("DatabaseNotFound", result.Error!.Code);
        Assert.Empty(world.Agent.PrivilegeChanges);
    }

    /// <summary>A failure of the agent leaves the recorded grant exactly as it was.</summary>
    [Fact]
    public async Task A_failure_of_the_agent_leaves_the_recorded_grant_exactly_as_it_was()
    {
        var world = await UsersTestWorld.CreateAsync();
        world.Agent.SetPrivilegesAnswers.Enqueue(Result.Fail(Error.Of("AgentNotFound", ErrorType.NotFound)));

        var result = await ChangeAsync(world, world.DedicatedUserId, world.OwnDatabaseId, DatabasePrivilege.Delete);

        Assert.Equal("AgentNotFound", result.Error!.Code);
        var dedicated = (await world.AllUsersAsync()).Single(user => { return user.Id == world.DedicatedUserId; });
        Assert.Equal(
            DatabasePrivilegeSet.Select | DatabasePrivilegeSet.Insert,
            Assert.Single(dedicated.Grants).Privileges);
    }

    /// <summary>A row that cannot be written puts the previous set back on the server.</summary>
    [Fact]
    public async Task A_row_that_cannot_be_written_puts_the_previous_set_back_on_the_server()
    {
        var failure = new DbUpdateException(
            "write failed",
            new PostgresException("write failed", "ERROR", "ERROR", PostgresErrorCodes.SerializationFailure));
        var world = await UsersTestWorld.CreateAsync(saveFailure: failure);

        var result = await ChangeAsync(world, world.DedicatedUserId, world.OwnDatabaseId, DatabasePrivilege.Delete);

        Assert.Equal("DatabasePrivilegesNotChanged", result.Error!.Code);
        Assert.Equal(ErrorType.Failure, result.Error.Type);
        Assert.Equal(2, world.Agent.PrivilegeChanges.Count);
        Assert.Equal([DatabasePrivilegeKind.Delete], world.Agent.PrivilegeChanges[0].Privileges);
        Assert.Equal(
            [DatabasePrivilegeKind.Select, DatabasePrivilegeKind.Insert],
            world.Agent.PrivilegeChanges[1].Privileges);
        Assert.False(Assert.Single(world.Audit.Entries).Succeeded);
    }

    /// <summary>The change is journalled with the user and database names and never the privileges.</summary>
    [Fact]
    public async Task The_change_is_journalled_with_the_user_and_database_names_and_never_the_privileges()
    {
        var world = await UsersTestWorld.CreateAsync();

        await ChangeAsync(world, world.ReaderUserId, world.OwnDatabaseId, DatabasePrivilege.Select);

        var entry = Assert.Single(world.Audit.Entries);
        Assert.Equal(DatabaseAuditActions.DatabasePrivilegesChanged, entry.Action);
        Assert.Equal("alice_reader:alice_shop", entry.Subject);
        Assert.True(entry.Succeeded);
        Assert.DoesNotContain("Select", entry.Subject, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Runs the handler as the owner with the usual audit context.</summary>
    /// <param name="world">The world to run in.</param>
    /// <param name="userId">The user whose privileges change.</param>
    /// <param name="databaseId">The database they are held on.</param>
    /// <param name="privileges">The complete set to hold.</param>
    private static async Task<Result> ChangeAsync(
        UsersTestWorld world,
        Guid userId,
        Guid databaseId,
        params DatabasePrivilege[] privileges)
    {
        var handler = new ChangeDatabaseUserPrivilegesCommandHandler(
            world.Context,
            world.Accounts,
            world.Agent,
            world.Journal,
            new FakeClock(UsersTestWorld.Now),
            UsersTestWorld.Log<ChangeDatabaseUserPrivilegesCommandHandler>());

        return await handler.HandleAsync(
            new ChangeDatabaseUserPrivilegesCommand(privileges, userId, databaseId, "203.0.113.7", "tests"),
            CancellationToken.None);
    }
}
