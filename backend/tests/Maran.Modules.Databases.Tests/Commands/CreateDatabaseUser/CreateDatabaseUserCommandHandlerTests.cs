using Maran.Modules.Databases.Commands.CreateDatabaseUser;
using Maran.Modules.Databases.Common;
using Maran.Modules.Databases.Domain.Entities;
using Maran.Modules.Databases.Domain.Policies;
using Maran.Modules.Databases.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Maran.Modules.Databases.Tests.Commands.CreateDatabaseUser;

/// <summary>What creating an additional database user refuses, in what order it touches the stores, and what it leaves behind.</summary>
public sealed class CreateDatabaseUserCommandHandlerTests
{
    /// <summary>Creating a user records it holding nothing and returns the password once.</summary>
    [Fact]
    public async Task Creating_a_user_records_it_holding_nothing_and_returns_the_password_once()
    {
        var world = await UsersTestWorld.CreateAsync();

        var result = await HandleAsync(world, "newuser");

        Assert.True(result.IsSuccess);
        Assert.Equal("alice_newuser", result.Value.FullName);
        Assert.Equal(UsersTestWorld.OwnerAccountId, result.Value.AccountId);

        // The value the agent installed is the value the customer is shown, or the customer has a
        // password that does not work.
        var call = Assert.Single(world.Agent.UserCreates);
        Assert.Equal("alice", call.AccountUsername);
        Assert.Equal("newuser", call.DbUsername);
        Assert.Equal(call.Password.Reveal(), result.Value.Password.Reveal());

        var row = (await world.AllUsersAsync()).Single(user => { return user.NameSuffix == "newuser"; });
        Assert.Empty(row.Grants);
        Assert.Equal("alice_newuser", row.FullName);
    }

    /// <summary>No mapped property of the new row holds the password.</summary>
    [Fact]
    public async Task No_mapped_property_of_the_new_row_holds_the_password()
    {
        var world = await UsersTestWorld.CreateAsync();

        var result = await HandleAsync(world, "newuser");

        using var read = DatabasesTestContext.Create(FakeCurrentUser.Admin(), world.Store);
        var row = await read.DatabaseUsers.SingleAsync(user => user.Id == result.Value.Id);
        foreach (var property in read.Entry(row).Properties)
        {
            Assert.NotEqual(result.Value.Password.Reveal(), property.CurrentValue as string);
        }
    }

    /// <summary>The creation is journalled by name and never by password.</summary>
    [Fact]
    public async Task The_creation_is_journalled_by_name_and_never_by_password()
    {
        var world = await UsersTestWorld.CreateAsync();

        var result = await HandleAsync(world, "newuser");

        var entry = Assert.Single(world.Audit.Entries);
        Assert.Equal(DatabaseAuditActions.DatabaseUserCreated, entry.Action);
        Assert.True(entry.Succeeded);
        Assert.Equal("alice_newuser", entry.Subject);
        Assert.DoesNotContain(result.Value.Password.Reveal(), entry.Subject, StringComparison.Ordinal);
    }

    /// <summary>A name the account already uses is refused before the agent is called.</summary>
    [Fact]
    public async Task A_name_the_account_already_uses_is_refused_before_the_agent_is_called()
    {
        var world = await UsersTestWorld.CreateAsync();

        var result = await HandleAsync(world, "reader");

        Assert.False(result.IsSuccess);
        Assert.Equal("DatabaseUserNameTaken", result.Error!.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Empty(world.Agent.UserCreates);
        Assert.False(Assert.Single(world.Audit.Entries).Succeeded);
    }

    /// <summary>A name a dedicated user already holds is refused as taken.</summary>
    [Fact]
    public async Task A_name_a_dedicated_user_already_holds_is_refused_as_taken()
    {
        var world = await UsersTestWorld.CreateAsync();

        var result = await HandleAsync(world, "shopuser");

        Assert.Equal("DatabaseUserNameTaken", result.Error!.Code);
        Assert.Empty(world.Agent.UserCreates);
    }

    /// <summary>A name another tenant uses is still available because names are prefixed.</summary>
    [Fact]
    public async Task A_name_another_tenant_uses_is_still_available_because_names_are_prefixed()
    {
        var world = await UsersTestWorld.CreateAsync();

        // bob's dedicated user is also called shopuser; alice's creating "shopuser" is refused above
        // only because alice has one herself. A name only bob has must be free for alice.
        var result = await HandleAsync(world, "onlybob");

        Assert.True(result.IsSuccess);
    }

    /// <summary>A name too long once the prefix is added is refused before the agent is called.</summary>
    [Fact]
    public async Task A_name_too_long_once_the_prefix_is_added_is_refused_before_the_agent_is_called()
    {
        var world = await UsersTestWorld.CreateAsync();

        var result = await HandleAsync(world, new string('a', 28));

        Assert.Equal("DatabaseUserNameTooLong", result.Error!.Code);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
        Assert.Empty(world.Agent.UserCreates);
    }

    /// <summary>An account the caller does not own is answered not found.</summary>
    [Fact]
    public async Task An_account_the_caller_does_not_own_is_answered_not_found()
    {
        var world = await UsersTestWorld.CreateAsync();
        var handler = new CreateDatabaseUserCommandHandler(
            world.Context,
            new StubAccountDirectory(),
            world.Agent,
            world.LimitGate,
            world.Journal,
            new FakeClock(UsersTestWorld.Now),
            UsersTestWorld.Log<CreateDatabaseUserCommandHandler>());

        var result = await handler.HandleAsync(
            new CreateDatabaseUserCommand(UsersTestWorld.StrangerAccountId, "intruder", "203.0.113.7", "tests"),
            CancellationToken.None);

        Assert.Equal("AccountNotFound", result.Error!.Code);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        Assert.Empty(world.Agent.UserCreates);
    }

    /// <summary>The allowance of additional users is enforced and a dedicated user is not counted against it.</summary>
    [Fact]
    public async Task The_allowance_of_additional_users_is_enforced_and_a_dedicated_user_is_not_counted_against_it()
    {
        var world = await UsersTestWorld.CreateAsync();
        for (var index = 1; index < DatabaseUserLimits.MaximumExtraUsersPerAccount; index++)
        {
            world.Context.DatabaseUsers.Add(new DatabaseUser(
                Guid.NewGuid(), UsersTestWorld.OwnerAccountId, $"extra{index}", $"alice_extra{index}", UsersTestWorld.Now));
        }

        await world.Context.SaveChangesAsync();

        // 49 added plus "reader" is 50 additional users, and the dedicated user is a 51st row that must
        // not count, so the account is exactly at its allowance.
        var refused = await HandleAsync(world, "onemore");

        Assert.Equal("DatabaseUserLimitReached", refused.Error!.Code);
        Assert.Equal(ErrorType.Conflict, refused.Error.Type);
        Assert.Empty(world.Agent.UserCreates);
    }

    /// <summary>One slot short of the allowance still creates, so the limit is not off by one.</summary>
    [Fact]
    public async Task One_slot_short_of_the_allowance_still_creates()
    {
        var world = await UsersTestWorld.CreateAsync();
        for (var index = 1; index < DatabaseUserLimits.MaximumExtraUsersPerAccount - 1; index++)
        {
            world.Context.DatabaseUsers.Add(new DatabaseUser(
                Guid.NewGuid(), UsersTestWorld.OwnerAccountId, $"extra{index}", $"alice_extra{index}", UsersTestWorld.Now));
        }

        await world.Context.SaveChangesAsync();

        var created = await HandleAsync(world, "lastone");

        Assert.True(created.IsSuccess);
    }

    /// <summary>A last slot taken by a concurrent creation drops the login this request made.</summary>
    [Fact]
    public async Task A_last_slot_taken_by_a_concurrent_creation_drops_the_login_this_request_made()
    {
        var world = await UsersTestWorld.CreateAsync();
        world.LimitGate.RefuseNextClaim = true;

        var result = await HandleAsync(world, "newuser");

        Assert.Equal("DatabaseUserLimitReached", result.Error!.Code);
        var drop = Assert.Single(world.Agent.UserDrops);
        Assert.Equal("newuser", drop.DbUsername);
        Assert.DoesNotContain(await world.AllUsersAsync(), user => { return user.NameSuffix == "newuser"; });
    }

    /// <summary>A failure of the agent is answered with its code and writes no row.</summary>
    [Fact]
    public async Task A_failure_of_the_agent_is_answered_with_its_code_and_writes_no_row()
    {
        var world = await UsersTestWorld.CreateAsync();
        world.Agent.CreateUserResult = Result<string>.Fail(Error.Of("AgentSystemFailure", ErrorType.Failure));

        var result = await HandleAsync(world, "newuser");

        Assert.Equal("AgentSystemFailure", result.Error!.Code);
        Assert.DoesNotContain(await world.AllUsersAsync(), user => { return user.NameSuffix == "newuser"; });
        Assert.Empty(world.Agent.UserDrops);
        Assert.False(Assert.Single(world.Audit.Entries).Succeeded);
    }

    /// <summary>A row that cannot be written drops the login so a retry starts clean.</summary>
    [Fact]
    public async Task A_row_that_cannot_be_written_drops_the_login_so_a_retry_starts_clean()
    {
        var failure = new DbUpdateException(
            "write failed",
            new PostgresException("write failed", "ERROR", "ERROR", PostgresErrorCodes.UniqueViolation));
        var world = await UsersTestWorld.CreateAsync(saveFailure: failure);

        var result = await HandleAsync(world, "newuser");

        Assert.Equal("DatabaseUserProvisioningFailed", result.Error!.Code);
        Assert.Equal(ErrorType.Failure, result.Error.Type);
        Assert.Single(world.Agent.UserDrops);
        Assert.False(Assert.Single(world.Audit.Entries).Succeeded);
    }

    /// <summary>Runs the handler as the owner with the usual audit context.</summary>
    /// <param name="world">The world to run in.</param>
    /// <param name="nameSuffix">The user name suffix.</param>
    private static async Task<Result<CreatedDatabaseUserDto>> HandleAsync(UsersTestWorld world, string nameSuffix)
    {
        var handler = new CreateDatabaseUserCommandHandler(
            world.Context,
            world.Accounts,
            world.Agent,
            world.LimitGate,
            world.Journal,
            new FakeClock(UsersTestWorld.Now),
            UsersTestWorld.Log<CreateDatabaseUserCommandHandler>());

        return await handler.HandleAsync(
            new CreateDatabaseUserCommand(UsersTestWorld.OwnerAccountId, nameSuffix, "203.0.113.7", "tests"),
            CancellationToken.None);
    }
}
