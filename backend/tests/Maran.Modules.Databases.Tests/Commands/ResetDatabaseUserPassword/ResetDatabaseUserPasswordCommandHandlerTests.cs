using Maran.Modules.Databases.Commands.ResetDatabaseUserPassword;
using Maran.Modules.Databases.Common;
using Maran.Modules.Databases.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Databases.Tests.Commands.ResetDatabaseUserPassword;

/// <summary>The recovery path of a lost database user password: what it changes, who may ask, what it refuses to keep.</summary>
public sealed class ResetDatabaseUserPasswordCommandHandlerTests
{
    /// <summary>Resetting returns the password the agent installed, once.</summary>
    [Fact]
    public async Task Resetting_returns_the_password_the_agent_installed_once()
    {
        var world = await UsersTestWorld.CreateAsync();

        var result = await ResetAsync(world, world.ReaderUserId);

        Assert.True(result.IsSuccess);
        Assert.Equal("alice_reader", result.Value.FullName);
        var call = Assert.Single(world.Agent.PasswordChanges);
        Assert.Equal(new { Account = "alice", User = "reader" }, new { Account = call.AccountUsername, User = call.DbUsername });
        Assert.Equal(call.Password.Reveal(), result.Value.Password.Reveal());
    }

    /// <summary>A dedicated user can be re-credentialed like any other.</summary>
    [Fact]
    public async Task A_dedicated_user_can_be_re_credentialed_like_any_other()
    {
        var world = await UsersTestWorld.CreateAsync();

        var result = await ResetAsync(world, world.DedicatedUserId);

        Assert.True(result.IsSuccess);
        Assert.Equal("shopuser", Assert.Single(world.Agent.PasswordChanges).DbUsername);
    }

    /// <summary>Another tenants user answers not found and no credential is issued.</summary>
    [Fact]
    public async Task Another_tenants_user_answers_not_found_and_no_credential_is_issued()
    {
        // The most valuable IDOR in the module: pointed at somebody else's row, this returns a WORKING
        // CREDENTIAL on their data, and a 403 would confirm the row is real.
        var world = await UsersTestWorld.CreateAsync();

        var result = await ResetAsync(world, world.StrangerUserId);

        Assert.Equal("DatabaseUserNotFound", result.Error!.Code);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        Assert.Empty(world.Agent.PasswordChanges);
    }

    /// <summary>A failure of the agent is answered with its code and no password.</summary>
    [Fact]
    public async Task A_failure_of_the_agent_is_answered_with_its_code_and_no_password()
    {
        var world = await UsersTestWorld.CreateAsync();
        world.Agent.SetPasswordResult = Result.Fail(Error.Of("AgentNotFound", ErrorType.NotFound));

        var result = await ResetAsync(world, world.ReaderUserId);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentNotFound", result.Error!.Code);
        Assert.False(Assert.Single(world.Audit.Entries).Succeeded);
    }

    /// <summary>The journal names the user and never the value.</summary>
    [Fact]
    public async Task The_journal_names_the_user_and_never_the_value()
    {
        var world = await UsersTestWorld.CreateAsync();

        var result = await ResetAsync(world, world.ReaderUserId);

        var entry = Assert.Single(world.Audit.Entries);
        Assert.Equal(DatabaseAuditActions.DatabaseUserPasswordReset, entry.Action);
        Assert.Equal("alice_reader", entry.Subject);
        Assert.True(entry.Succeeded);
        Assert.DoesNotContain(result.Value.Password.Reveal(), entry.Subject, StringComparison.Ordinal);
    }

    /// <summary>Runs the handler as the owner with the usual audit context.</summary>
    /// <param name="world">The world to run in.</param>
    /// <param name="userId">The user to re-credential.</param>
    private static async Task<Result<DatabaseUserPasswordDto>> ResetAsync(UsersTestWorld world, Guid userId)
    {
        var handler = new ResetDatabaseUserPasswordCommandHandler(world.Context, world.Accounts, world.Agent, world.Journal);

        return await handler.HandleAsync(
            new ResetDatabaseUserPasswordCommand(userId, "203.0.113.7", "tests"), CancellationToken.None);
    }
}
