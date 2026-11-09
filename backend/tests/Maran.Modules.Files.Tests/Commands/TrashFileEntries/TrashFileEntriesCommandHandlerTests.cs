using Maran.Agent.Client.Interfaces;
using Maran.Modules.Files.Commands.TrashFileEntries;
using Maran.Modules.Files.Options;
using Maran.Modules.Files.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maran.Modules.Files.Tests.Commands.TrashFileEntries;

/// <summary>
/// Holds <see cref="TrashFileEntriesCommandHandler"/> to its contract for trashing: the account is resolved first, the agent
/// is addressed as that account's own user, a failure of one item does not stop the others, and every item is audited.
/// </summary>
public sealed class TrashFileEntriesCommandHandlerTests
{
    /// <summary>Builds the handler over a world's collaborators.</summary>
    /// <param name="world">The world.</param>
    /// <returns>The handler.</returns>
    private static TrashFileEntriesCommandHandler HandlerOf(FilesHandlerWorld world)
    {
        return new TrashFileEntriesCommandHandler(
            world.Gate, world.Runner, world.Client, NullLogger<TrashFileEntriesCommandHandler>.Instance);
    }

    /// <summary>Builds a command that names the given items.</summary>
    /// <param name="accountId">The account the command is on.</param>
    /// <param name="items">The paths (or trash ids) the batch names.</param>
    /// <returns>The command.</returns>
    private static TrashFileEntriesCommand CommandOf(Guid accountId, params string[] items)
    {
        return new TrashFileEntriesCommand(
            accountId, items, FilesHandlerWorld.Address, FilesHandlerWorld.UserAgent);
    }

    /// <summary>Trashing: each item reaches the agent as the account's own user and is audited on its own.</summary>
    [Fact]
    public async Task Every_item_reaches_the_agent_as_the_accounts_own_user_and_is_audited()
    {
        var world = FilesHandlerWorld.ForCustomer();

        var result = await HandlerOf(world).HandleAsync(CommandOf(world.Active.Id, "a.txt", "dir/b.txt"), default);

        Assert.True(result.IsSuccess);
        Assert.Equal([true, true], result.Value.Items.Select(item =>
        {
            return item.Succeeded;
        }));
        Assert.Equal(["alpha:a.txt", "alpha:dir/b.txt"], world.Client.Calls.Select(call =>
        {
            return $"{call.Account}:{call.Path}";
        }));
        Assert.All(world.Client.Calls, call =>
        {
            Assert.Equal(nameof(IAgentFilesClient.TrashEntryAsync), call.Operation);
        });
        Assert.Equal(["alpha:a.txt", "alpha:dir/b.txt"], world.Writer.Entries.Select(entry =>
        {
            return entry.Subject;
        }));
        Assert.All(world.Writer.Entries, entry =>
        {
            Assert.Equal(FileAuditActions.FileTrashed, entry.Action);
            Assert.True(entry.Succeeded);
            Assert.Equal(world.Active.Id, entry.AccountId);
            Assert.Equal(FilesHandlerWorld.Address, entry.IpAddress);
        });
    }

    /// <summary>A refusal of one item is reported for that item and the others still run.</summary>
    [Fact]
    public async Task A_refusal_of_one_item_is_reported_for_it_and_the_others_still_run()
    {
        var world = FilesHandlerWorld.ForCustomer();
        world.Client.FailOn(nameof(IAgentFilesClient.TrashEntryAsync), "dir/b.txt", Error.Of("AgentNotFound", ErrorType.NotFound));

        var result = await HandlerOf(world).HandleAsync(CommandOf(world.Active.Id, "a.txt", "dir/b.txt", "c.txt"), default);

        Assert.True(result.IsSuccess);
        var items = result.Value.Items;
        Assert.Equal([true, false, true], items.Select(item =>
        {
            return item.Succeeded;
        }));
        Assert.Equal("FilesEntryNotFound", items[1].Code);
        Assert.Equal(3, world.Client.Calls.Count);
        Assert.Equal([true, false, true], world.Writer.Entries.Select(entry =>
        {
            return entry.Succeeded;
        }));
    }

    /// <summary>A batch over the configured ceiling is refused before the agent is asked anything.</summary>
    [Fact]
    public async Task A_batch_over_the_ceiling_is_refused_before_the_agent_is_asked_anything()
    {
        var world = FilesHandlerWorld.ForCustomer(new FilesOptions { MaxBatchItems = 2 });

        var result = await HandlerOf(world).HandleAsync(CommandOf(world.Active.Id, "a.txt", "dir/b.txt", "c.txt"), default);

        Assert.Equal("FilesBatchTooLarge", result.Error!.Code);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
        Assert.Empty(world.Client.Calls);
    }

    /// <summary>Another tenant's account is answered not found, nothing reaches the agent, and the probe is journalled.</summary>
    [Fact]
    public async Task Another_tenants_account_is_not_found_and_the_agent_is_never_asked()
    {
        var world = FilesHandlerWorld.ForCustomer();

        var result = await HandlerOf(world).HandleAsync(CommandOf(world.Stranger, "a.txt"), default);

        Assert.Equal("AccountNotFound", result.Error!.Code);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        Assert.Empty(world.Client.Calls);
        var entry = Assert.Single(world.Writer.Entries);
        Assert.False(entry.Succeeded);
        Assert.Equal(world.Stranger.ToString(), entry.Subject);
    }

    /// <summary>A customer on a suspended account is refused with a conflict and the agent is never asked.</summary>
    [Fact]
    public async Task A_customer_on_a_suspended_account_is_refused()
    {
        var world = FilesHandlerWorld.ForCustomer();

        var result = await HandlerOf(world).HandleAsync(CommandOf(world.Suspended.Id, "a.txt"), default);

        Assert.Equal("FilesAccountSuspended", result.Error!.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Empty(world.Client.Calls);
    }

    /// <summary>An administrator may act on a suspended account.</summary>
    [Fact]
    public async Task An_administrator_may_act_on_a_suspended_account()
    {
        var world = FilesHandlerWorld.ForAdministrator();

        var result = await HandlerOf(world).HandleAsync(CommandOf(world.Suspended.Id, "a.txt"), default);

        Assert.True(result.IsSuccess);
        var call = Assert.Single(world.Client.Calls);
        Assert.Equal("beta", call.Account);
    }
}
