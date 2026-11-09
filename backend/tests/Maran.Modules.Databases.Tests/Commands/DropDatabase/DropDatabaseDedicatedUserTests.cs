using Maran.Modules.Databases.Commands.DropDatabase;
using Maran.Modules.Databases.Tests.TestSupport;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Databases.Tests.Commands.DropDatabase;

/// <summary>That dropping a database takes its dedicated user and every grant on it, and only those.</summary>
public sealed class DropDatabaseDedicatedUserTests
{
    /// <summary>Dropping a database removes its dedicated user row and keeps the accounts other users.</summary>
    [Fact]
    public async Task Dropping_a_database_removes_its_dedicated_user_row_and_keeps_the_accounts_other_users()
    {
        var world = await UsersTestWorld.CreateAsync();

        var result = await DropAsync(world, world.OwnDatabaseId);

        Assert.True(result.IsSuccess);
        var remaining = await world.AllUsersAsync();
        Assert.DoesNotContain(remaining, user => { return user.Id == world.DedicatedUserId; });
        Assert.Contains(remaining, user => { return user.Id == world.ReaderUserId; });
        Assert.Contains(remaining, user => { return user.Id == world.StrangerUserId; });
    }

    /// <summary>The other tenants user and grant survive.</summary>
    [Fact]
    public async Task The_other_tenants_user_and_grant_survive()
    {
        var world = await UsersTestWorld.CreateAsync();

        await DropAsync(world, world.OwnDatabaseId);

        var stranger = (await world.AllUsersAsync()).Single(user => { return user.Id == world.StrangerUserId; });
        Assert.Single(stranger.Grants);
    }

    /// <summary>A refused drop leaves the user row where it was.</summary>
    [Fact]
    public async Task A_refused_drop_leaves_the_user_row_where_it_was()
    {
        var world = await UsersTestWorld.CreateAsync();

        var result = await DropAsync(world, world.StrangerDatabaseId);

        Assert.False(result.IsSuccess);
        Assert.Contains(await world.AllUsersAsync(), user => { return user.Id == world.DedicatedUserId; });
        Assert.Contains(await world.AllUsersAsync(), user => { return user.Id == world.StrangerUserId; });
    }

    /// <summary>Runs the handler as the owner.</summary>
    /// <param name="world">The world to run in.</param>
    /// <param name="databaseId">The database to drop.</param>
    private static async Task<Result> DropAsync(UsersTestWorld world, Guid databaseId)
    {
        var handler = new DropDatabaseCommandHandler(world.Context, world.Accounts, world.Agent, world.Journal);

        return await handler.HandleAsync(
            new DropDatabaseCommand(databaseId, "203.0.113.7", "tests"), CancellationToken.None);
    }
}
