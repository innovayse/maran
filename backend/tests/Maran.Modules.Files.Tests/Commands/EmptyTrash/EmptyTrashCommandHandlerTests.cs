using Maran.Agent.Client.Interfaces;
using Maran.Modules.Files.Commands.EmptyTrash;
using Maran.Modules.Files.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maran.Modules.Files.Tests.Commands.EmptyTrash;

/// <summary>
/// Holds <see cref="EmptyTrashCommandHandler"/> to emptying only the caller's own account's trash, reporting
/// how many entries went, and auditing the outcome whether it succeeded or not.
/// </summary>
public sealed class EmptyTrashCommandHandlerTests
{
    /// <summary>Builds the handler over a world's collaborators.</summary>
    /// <param name="world">The world.</param>
    /// <returns>The handler.</returns>
    private static EmptyTrashCommandHandler HandlerOf(FilesHandlerWorld world)
    {
        return new EmptyTrashCommandHandler(
            world.Gate, world.Client, world.Journal, NullLogger<EmptyTrashCommandHandler>.Instance);
    }

    /// <summary>Builds an empty-trash command.</summary>
    /// <param name="accountId">The account.</param>
    /// <returns>The command.</returns>
    private static EmptyTrashCommand CommandOf(Guid accountId)
    {
        return new EmptyTrashCommand(accountId, FilesHandlerWorld.Address, FilesHandlerWorld.UserAgent);
    }

    /// <summary>The count the agent reports is the count the customer is told, and one entry is audited.</summary>
    [Fact]
    public async Task Emptying_the_trash_reports_the_agents_count_and_audits_it()
    {
        var world = FilesHandlerWorld.ForCustomer();
        world.Client.EmptiedCount = 4;

        var result = await HandlerOf(world).HandleAsync(CommandOf(world.Active.Id), default);

        Assert.Equal(4, result.Value.Count);
        var call = Assert.Single(world.Client.Calls);
        Assert.Equal(nameof(IAgentFilesClient.EmptyTrashAsync), call.Operation);
        Assert.Equal("alpha", call.Account);
        var entry = Assert.Single(world.Writer.Entries);
        Assert.Equal(FileAuditActions.FileTrashEmptied, entry.Action);
        Assert.Equal("alpha:", entry.Subject);
        Assert.True(entry.Succeeded);
        Assert.Equal(world.Active.Id, entry.AccountId);
    }

    /// <summary>A busy agent is a typed unavailability and the attempt is audited as a failure.</summary>
    [Fact]
    public async Task A_busy_agent_is_reported_as_busy_and_the_attempt_is_audited_as_a_failure()
    {
        var world = FilesHandlerWorld.ForCustomer();
        world.Client.FailOn(
            nameof(IAgentFilesClient.EmptyTrashAsync), string.Empty, Error.Of("AgentSaturated", ErrorType.Unavailable));

        var result = await HandlerOf(world).HandleAsync(CommandOf(world.Active.Id), default);

        Assert.Equal("FilesServerBusy", result.Error!.Code);
        Assert.Equal(ErrorType.Unavailable, result.Error.Type);
        Assert.False(Assert.Single(world.Writer.Entries).Succeeded);
    }

    /// <summary>Another tenant's account is not found and the agent is never asked.</summary>
    [Fact]
    public async Task Another_tenants_account_is_not_found_and_the_agent_is_never_asked()
    {
        var world = FilesHandlerWorld.ForCustomer();

        var result = await HandlerOf(world).HandleAsync(CommandOf(world.Stranger), default);

        Assert.Equal("AccountNotFound", result.Error!.Code);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        Assert.Empty(world.Client.Calls);
    }

    /// <summary>A suspended account refuses a customer and admits an administrator.</summary>
    [Fact]
    public async Task A_suspended_account_refuses_a_customer_and_admits_an_administrator()
    {
        var customer = FilesHandlerWorld.ForCustomer();
        var administrator = FilesHandlerWorld.ForAdministrator();

        var refused = await HandlerOf(customer).HandleAsync(CommandOf(customer.Suspended.Id), default);
        var admitted = await HandlerOf(administrator).HandleAsync(CommandOf(administrator.Suspended.Id), default);

        Assert.Equal("FilesAccountSuspended", refused.Error!.Code);
        Assert.Equal(ErrorType.Conflict, refused.Error.Type);
        Assert.Empty(customer.Client.Calls);
        Assert.True(admitted.IsSuccess);
        Assert.Equal("beta", Assert.Single(administrator.Client.Calls).Account);
    }
}
