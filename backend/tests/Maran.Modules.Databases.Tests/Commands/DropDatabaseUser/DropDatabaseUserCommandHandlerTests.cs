using Maran.Modules.Databases.Commands.DropDatabaseUser;
using Maran.Modules.Databases.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Databases.Tests.Commands.DropDatabaseUser;

/// <summary>Who may drop which database user, and what a drop leaves behind.</summary>
public sealed class DropDatabaseUserCommandHandlerTests
{
    /// <summary>Dropping an additional user removes it on the server and then its row.</summary>
    [Fact]
    public async Task Dropping_an_additional_user_removes_it_on_the_server_and_then_its_row()
    {
        var world = await UsersTestWorld.CreateAsync();

        var result = await DropAsync(world, world.ReaderUserId);

        Assert.True(result.IsSuccess);
        var call = Assert.Single(world.Agent.UserDrops);
        Assert.Equal(new AgentUserDropCall("alice", "reader"), call);
        Assert.DoesNotContain(await world.AllUsersAsync(), user => { return user.Id == world.ReaderUserId; });
    }

    /// <summary>Dropping a user removes its grants with it and leaves other users alone.</summary>
    [Fact]
    public async Task Dropping_a_user_removes_its_grants_and_leaves_other_users_alone()
    {
        var world = await UsersTestWorld.CreateAsync();
        var before = (await world.AllUsersAsync()).Count;

        await DropAsync(world, world.ReaderUserId);

        var after = await world.AllUsersAsync();
        Assert.Equal(before - 1, after.Count);
        Assert.Contains(after, user => { return user.Id == world.DedicatedUserId && user.Grants.Count == 1; });
        Assert.Contains(after, user => { return user.Id == world.StrangerUserId; });
    }

    /// <summary>A databases dedicated user is refused with a conflict and nothing is touched.</summary>
    [Fact]
    public async Task A_databases_dedicated_user_is_refused_with_a_conflict_and_nothing_is_touched()
    {
        var world = await UsersTestWorld.CreateAsync();

        var result = await DropAsync(world, world.DedicatedUserId);

        Assert.False(result.IsSuccess);
        Assert.Equal("DatabaseUserIsDedicated", result.Error!.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Empty(world.Agent.UserDrops);
        Assert.Contains(await world.AllUsersAsync(), user => { return user.Id == world.DedicatedUserId; });
    }

    /// <summary>Another tenants user answers not found rather than forbidden and nothing is touched.</summary>
    [Fact]
    public async Task Another_tenants_user_answers_not_found_rather_than_forbidden_and_nothing_is_touched()
    {
        var world = await UsersTestWorld.CreateAsync();

        var result = await DropAsync(world, world.StrangerUserId);

        Assert.Equal("DatabaseUserNotFound", result.Error!.Code);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        Assert.Empty(world.Agent.UserDrops);
        Assert.Contains(await world.AllUsersAsync(), user => { return user.Id == world.StrangerUserId; });
    }

    /// <summary>An identifier that names nothing answers exactly as another tenants does.</summary>
    [Fact]
    public async Task An_identifier_that_names_nothing_answers_exactly_as_another_tenants_does()
    {
        var world = await UsersTestWorld.CreateAsync();

        var result = await DropAsync(world, Guid.NewGuid());

        Assert.Equal("DatabaseUserNotFound", result.Error!.Code);
    }

    /// <summary>A failure of the agent keeps the row so the drop can be retried.</summary>
    [Fact]
    public async Task A_failure_of_the_agent_keeps_the_row_so_the_drop_can_be_retried()
    {
        var world = await UsersTestWorld.CreateAsync();
        world.Agent.DropUserResult = Result.Fail(Error.Of("AgentSystemFailure", ErrorType.Failure));

        var result = await DropAsync(world, world.ReaderUserId);

        Assert.Equal("AgentSystemFailure", result.Error!.Code);
        Assert.Contains(await world.AllUsersAsync(), user => { return user.Id == world.ReaderUserId; });
    }

    /// <summary>Every outcome is journalled with the user's name and the right verdict.</summary>
    [Fact]
    public async Task Every_outcome_is_journalled_with_the_users_name_and_the_right_verdict()
    {
        var world = await UsersTestWorld.CreateAsync();

        await DropAsync(world, world.ReaderUserId);
        await DropAsync(world, world.DedicatedUserId);

        Assert.Collection(
            world.Audit.Entries,
            success =>
            {
                Assert.Equal(DatabaseAuditActions.DatabaseUserDropped, success.Action);
                Assert.Equal("alice_reader", success.Subject);
                Assert.True(success.Succeeded);
            },
            refusal =>
            {
                Assert.Equal(DatabaseAuditActions.DatabaseUserDropped, refusal.Action);
                Assert.Equal("alice_shopuser", refusal.Subject);
                Assert.False(refusal.Succeeded);
            });
    }

    /// <summary>Runs the handler as the owner with the usual audit context.</summary>
    /// <param name="world">The world to run in.</param>
    /// <param name="userId">The user to drop.</param>
    private static async Task<Result> DropAsync(UsersTestWorld world, Guid userId)
    {
        var handler = new DropDatabaseUserCommandHandler(world.Context, world.Accounts, world.Agent, world.Journal);

        return await handler.HandleAsync(
            new DropDatabaseUserCommand(userId, "203.0.113.7", "tests"), CancellationToken.None);
    }
}
