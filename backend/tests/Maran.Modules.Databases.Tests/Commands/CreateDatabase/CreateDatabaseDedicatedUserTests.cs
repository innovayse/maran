using Maran.Modules.Databases.Commands.CreateDatabase;
using Maran.Modules.Databases.Common;
using Maran.Modules.Databases.Domain.Policies;
using Maran.Modules.Databases.Tests.TestSupport;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Databases.Tests.Commands.CreateDatabase;

/// <summary>That a created database brings its dedicated user into the users list, in the same write.</summary>
public sealed class CreateDatabaseDedicatedUserTests
{
    /// <summary>Creating a database records its dedicated user holding every privilege on it.</summary>
    [Fact]
    public async Task Creating_a_database_records_its_dedicated_user_holding_every_privilege_on_it()
    {
        var world = await UsersTestWorld.CreateAsync();

        var result = await CreateAsync(world, "blog", "bloguser");

        Assert.True(result.IsSuccess);
        var user = (await world.AllUsersAsync()).Single(row => { return row.NameSuffix == "bloguser"; });
        Assert.Equal("alice_bloguser", user.FullName);
        Assert.Equal(UsersTestWorld.OwnerAccountId, user.AccountId);

        var grant = Assert.Single(user.Grants);
        Assert.Equal(result.Value.Id, grant.DatabaseId);
        Assert.Equal(DatabasePrivilegeSets.Full, grant.Privileges);
    }

    /// <summary>A user name an additional user already holds is refused before the agent is called.</summary>
    [Fact]
    public async Task A_user_name_an_additional_user_already_holds_is_refused_before_the_agent_is_called()
    {
        var world = await UsersTestWorld.CreateAsync();

        var result = await CreateAsync(world, "blog", "reader");

        Assert.Equal("DatabaseUserNameTaken", result.Error!.Code);
        Assert.Empty(world.Agent.Creates);
    }

    /// <summary>A refused plan slot leaves neither a database nor a user row.</summary>
    [Fact]
    public async Task A_refused_plan_slot_leaves_neither_a_database_nor_a_user_row()
    {
        var world = await UsersTestWorld.CreateAsync();
        var slotGate = new LocklessDatabaseSlotGate(world.Context) { RefuseNextClaim = true };

        var result = await CreateAsync(world, "blog", "bloguser", slotGate);

        Assert.Equal("DatabaseLimitReachedConcurrently", result.Error!.Code);
        Assert.DoesNotContain(await world.AllUsersAsync(), user => { return user.NameSuffix == "bloguser"; });
    }

    /// <summary>Runs the handler as the owner.</summary>
    /// <param name="world">The world to run in.</param>
    /// <param name="name">The database name suffix.</param>
    /// <param name="dbUserName">The dedicated user's name suffix.</param>
    /// <param name="slotGate">The gate to claim through; the lockless one by default.</param>
    private static async Task<Result<CreatedDatabaseDto>> CreateAsync(
        UsersTestWorld world,
        string name,
        string dbUserName,
        LocklessDatabaseSlotGate? slotGate = null)
    {
        var handler = new CreateDatabaseCommandHandler(
            world.Context,
            world.Accounts,
            world.Agent,
            slotGate ?? new LocklessDatabaseSlotGate(world.Context),
            world.Journal,
            new FakeClock(UsersTestWorld.Now),
            UsersTestWorld.Log<CreateDatabaseCommandHandler>());

        return await handler.HandleAsync(
            new CreateDatabaseCommand(UsersTestWorld.OwnerAccountId, name, dbUserName, "203.0.113.7", "tests"),
            CancellationToken.None);
    }
}
