using Maran.Agent.Client.Interfaces;
using Maran.Modules.Files.Commands.RenameFileEntry;
using Maran.Modules.Files.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maran.Modules.Files.Tests.Commands.RenameFileEntry;

/// <summary>
/// Holds <see cref="RenameFileEntryCommandHandler"/> to renaming inside the entry's own folder, reading the
/// renamed entry back, answering a clash as a conflict and auditing both names.
/// </summary>
public sealed class RenameFileEntryCommandHandlerTests
{
    /// <summary>Builds the handler over a world's collaborators.</summary>
    /// <param name="world">The world.</param>
    /// <returns>The handler.</returns>
    private static RenameFileEntryCommandHandler HandlerOf(FilesHandlerWorld world)
    {
        return new RenameFileEntryCommandHandler(
            world.Gate, world.Client, world.Reader, world.Journal, NullLogger<RenameFileEntryCommandHandler>.Instance);
    }

    /// <summary>Builds a rename command.</summary>
    /// <param name="accountId">The account.</param>
    /// <param name="path">The entry's current path.</param>
    /// <param name="newName">The new single-segment name.</param>
    /// <returns>The command.</returns>
    private static RenameFileEntryCommand CommandOf(Guid accountId, string path, string newName)
    {
        return new RenameFileEntryCommand(
            accountId, path, newName, FilesHandlerWorld.Address, FilesHandlerWorld.UserAgent);
    }

    /// <summary>A rename keeps the entry in its own folder and audits old and new names.</summary>
    [Fact]
    public async Task Renaming_keeps_the_entry_in_its_folder_and_audits_both_names()
    {
        var world = FilesHandlerWorld.ForCustomer();
        world.Client.AddFile("docs", "new.txt", 12, 0b110_100_100, DateTimeOffset.UnixEpoch);

        var result = await HandlerOf(world).HandleAsync(CommandOf(world.Active.Id, "docs/old.txt", "new.txt"), default);

        Assert.True(result.IsSuccess);
        Assert.Equal("docs/new.txt", result.Value.Path);
        var moved = world.Client.Calls.Single(call =>
        {
            return call.Operation == nameof(IAgentFilesClient.MoveEntryAsync);
        });
        Assert.Equal("docs/old.txt", moved.Path);
        Assert.Equal("docs/new.txt", moved.Destination);
        var entry = Assert.Single(world.Writer.Entries);
        Assert.Equal(FileAuditActions.FileRenamed, entry.Action);
        Assert.Equal("alpha:docs/old.txt -> docs/new.txt", entry.Subject);
        Assert.True(entry.Succeeded);
    }

    /// <summary>A name that is taken is a conflict, and the failure is audited.</summary>
    [Fact]
    public async Task A_name_that_is_taken_is_a_conflict_and_the_failure_is_audited()
    {
        var world = FilesHandlerWorld.ForCustomer();
        world.Client.FailOn(
            nameof(IAgentFilesClient.MoveEntryAsync), string.Empty, Error.Of("AgentAlreadyExists", ErrorType.Conflict));

        var result = await HandlerOf(world).HandleAsync(CommandOf(world.Active.Id, "old.txt", "new.txt"), default);

        Assert.Equal("FilesEntryExists", result.Error!.Code);
        Assert.False(Assert.Single(world.Writer.Entries).Succeeded);
    }

    /// <summary>An entry that is gone is answered not found.</summary>
    [Fact]
    public async Task An_entry_that_is_gone_is_answered_not_found()
    {
        var world = FilesHandlerWorld.ForCustomer();
        world.Client.FailOn(
            nameof(IAgentFilesClient.MoveEntryAsync), string.Empty, Error.Of("AgentNotFound", ErrorType.NotFound));

        var result = await HandlerOf(world).HandleAsync(CommandOf(world.Active.Id, "old.txt", "new.txt"), default);

        Assert.Equal("FilesEntryNotFound", result.Error!.Code);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
    }

    /// <summary>Another tenant's account is not found and the agent is never asked.</summary>
    [Fact]
    public async Task Another_tenants_account_is_not_found_and_the_agent_is_never_asked()
    {
        var world = FilesHandlerWorld.ForCustomer();

        var result = await HandlerOf(world).HandleAsync(CommandOf(world.Stranger, "old.txt", "new.txt"), default);

        Assert.Equal("AccountNotFound", result.Error!.Code);
        Assert.Empty(world.Client.Calls);
    }

    /// <summary>A customer on a suspended account is refused before the agent is asked.</summary>
    [Fact]
    public async Task A_customer_on_a_suspended_account_is_refused()
    {
        var world = FilesHandlerWorld.ForCustomer();

        var result = await HandlerOf(world).HandleAsync(CommandOf(world.Suspended.Id, "old.txt", "new.txt"), default);

        Assert.Equal("FilesAccountSuspended", result.Error!.Code);
        Assert.Empty(world.Client.Calls);
    }

    /// <summary>An administrator may rename on a suspended account.</summary>
    [Fact]
    public async Task An_administrator_may_rename_on_a_suspended_account()
    {
        var world = FilesHandlerWorld.ForAdministrator();
        world.Client.AddFile("", "new.txt", 1, 0b110_100_100, DateTimeOffset.UnixEpoch);

        var result = await HandlerOf(world).HandleAsync(CommandOf(world.Suspended.Id, "old.txt", "new.txt"), default);

        Assert.True(result.IsSuccess);
    }
}
