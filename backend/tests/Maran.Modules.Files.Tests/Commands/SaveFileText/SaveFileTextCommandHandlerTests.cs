using Maran.Modules.Files.Commands.SaveFileText;
using Maran.Modules.Files.Options;
using Maran.Modules.Files.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maran.Modules.Files.Tests.Commands.SaveFileText;

/// <summary>What saving an edited text file writes, refuses and records.</summary>
public sealed class SaveFileTextCommandHandlerTests
{
    /// <summary>Builds the handler over a world.</summary>
    /// <param name="world">The world the handler works in.</param>
    /// <returns>The handler.</returns>
    private static SaveFileTextCommandHandler HandlerOver(FilesWorld world)
    {
        return new SaveFileTextCommandHandler(
            world.Gate,
            world.Lookup,
            world.Agent,
            world.Journal,
            world.Options,
            NullLogger<SaveFileTextCommandHandler>.Instance);
    }

    /// <summary>A command as the action stamps it.</summary>
    /// <param name="accountId">The account asked about.</param>
    /// <param name="content">The new text.</param>
    /// <param name="expected">The modification time the editor loaded the file at, if any.</param>
    /// <returns>The command.</returns>
    private static SaveFileTextCommand Command(Guid accountId, string content = "new text", DateTimeOffset? expected = null)
    {
        return new SaveFileTextCommand(
            accountId, "notes.txt", content, expected, FilesWorld.Origin.IpAddress, FilesWorld.Origin.UserAgent);
    }

    /// <summary>Another tenant's account is not found, nothing is written and the refusal is journalled under the save.</summary>
    [Fact]
    public async Task Another_tenants_account_is_not_found_and_nothing_is_written()
    {
        var world = new FilesWorld();
        world.Agent.Put(string.Empty, FilesWorld.File("notes.txt"));

        var result = await HandlerOver(world).HandleAsync(Command(FilesWorld.StrangerAccountId), default);

        Assert.Equal("AccountNotFound", result.Error!.Code);
        Assert.Empty(world.Agent.Calls);
        var entry = Assert.Single(world.Writer.Entries);
        Assert.Equal(FileAuditActions.FileSaved, entry.Action);
        Assert.False(entry.Succeeded);
    }

    /// <summary>The control: a save of the caller's own file keeps the file's mode, and is journalled as a success.</summary>
    [Fact]
    public async Task A_save_keeps_the_mode_the_file_has_and_is_journalled()
    {
        var world = new FilesWorld();
        world.Agent.Put(string.Empty, FilesWorld.File("notes.txt", 3, 0b111_101_000));

        var result = await HandlerOver(world).HandleAsync(Command(FilesWorld.OwnAccountId, expected: FilesWorld.Modified), default);

        Assert.True(result.IsSuccess);
        var write = Assert.Single(world.Agent.TextWrites);
        Assert.Equal(("notes.txt", "new text", 0b111_101_000u), write);
        Assert.Equal(8ul, result.Value.SizeBytes);
        Assert.Equal(FilesWorld.Modified, result.Value.ModifiedAt);
        var entry = Assert.Single(world.Writer.Entries);
        Assert.Equal(FileAuditActions.FileSaved, entry.Action);
        Assert.True(entry.Succeeded);
        Assert.Equal("alice:notes.txt", entry.Subject);
    }

    /// <summary>The content of the file never reaches the journal.</summary>
    [Fact]
    public async Task The_content_never_reaches_the_journal()
    {
        var world = new FilesWorld();
        world.Agent.Put(string.Empty, FilesWorld.File("notes.txt"));

        await HandlerOver(world).HandleAsync(Command(FilesWorld.OwnAccountId, "password=hunter2"), default);

        Assert.DoesNotContain(world.Writer.Entries, entry => { return entry.Subject.Contains("hunter2"); });
    }

    /// <summary>A file that has changed since the editor loaded it is a conflict and is not overwritten.</summary>
    [Fact]
    public async Task A_file_that_changed_since_it_was_loaded_is_a_conflict_and_is_not_overwritten()
    {
        var world = new FilesWorld();
        world.Agent.Put(string.Empty, FilesWorld.File("notes.txt"));

        var result = await HandlerOver(world).HandleAsync(
            Command(FilesWorld.OwnAccountId, expected: FilesWorld.Modified.AddSeconds(-1)), default);

        Assert.Equal("FilesEditConflict", result.Error!.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Empty(world.Agent.TextWrites);
    }

    /// <summary>A file the editor loaded and that has since been removed is a conflict, not a resurrection.</summary>
    [Fact]
    public async Task A_file_removed_since_it_was_loaded_is_a_conflict()
    {
        var world = new FilesWorld();
        world.Agent.Put(string.Empty, FilesWorld.File("other.txt"));

        var result = await HandlerOver(world).HandleAsync(
            Command(FilesWorld.OwnAccountId, expected: FilesWorld.Modified), default);

        Assert.Equal("FilesEditConflict", result.Error!.Code);
        Assert.Empty(world.Agent.TextWrites);
    }

    /// <summary>A file that does not exist yet is created with the default mode when nothing was expected to exist.</summary>
    [Fact]
    public async Task A_new_file_is_created_with_the_default_mode()
    {
        var world = new FilesWorld();
        world.Agent.Put(string.Empty, FilesWorld.File("other.txt"));

        var result = await HandlerOver(world).HandleAsync(Command(FilesWorld.OwnAccountId), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(0b110_100_100u, Assert.Single(world.Agent.TextWrites).Mode);
    }

    /// <summary>Text over the edit cap, counted in UTF-8 bytes, is refused before the agent is asked to write.</summary>
    [Fact]
    public async Task Text_over_the_edit_cap_counted_in_bytes_is_refused()
    {
        var world = new FilesWorld(options: new FilesOptions { MaxEditBytes = 4 });
        world.Agent.Put(string.Empty, FilesWorld.File("notes.txt"));

        // Three characters, six bytes.
        var result = await HandlerOver(world).HandleAsync(Command(FilesWorld.OwnAccountId, "ééé"), default);

        Assert.Equal("FilesNotEditable", result.Error!.Code);
        Assert.Empty(world.Agent.Calls);
    }

    /// <summary>A folder at the path is not a file to save into.</summary>
    [Fact]
    public async Task A_folder_at_the_path_is_not_editable()
    {
        var world = new FilesWorld();
        world.Agent.Put(string.Empty, FilesWorld.Folder("notes.txt"));

        var result = await HandlerOver(world).HandleAsync(Command(FilesWorld.OwnAccountId), default);

        Assert.Equal("FilesNotEditable", result.Error!.Code);
        Assert.Empty(world.Agent.TextWrites);
    }

    /// <summary>An agent refusal of the write is translated and journalled as a failed save.</summary>
    [Fact]
    public async Task An_agent_refusal_of_the_write_is_translated_and_journalled_as_a_failed_save()
    {
        var world = new FilesWorld();
        world.Agent.Put(string.Empty, FilesWorld.File("notes.txt"));
        world.Agent.WriteRefusal = Error.Of("AgentBelowFreeSpaceFloor", ErrorType.Failure);

        var result = await HandlerOver(world).HandleAsync(Command(FilesWorld.OwnAccountId), default);

        Assert.Equal("FilesQuotaExceeded", result.Error!.Code);
        var entry = Assert.Single(world.Writer.Entries);
        Assert.Equal(FileAuditActions.FileSaved, entry.Action);
        Assert.False(entry.Succeeded);
    }

    /// <summary>A customer on a suspended account is refused before the agent is asked anything.</summary>
    [Fact]
    public async Task A_suspended_account_is_refused_to_its_customer()
    {
        var world = new FilesWorld(AccountState.Suspended);
        world.Agent.Put(string.Empty, FilesWorld.File("notes.txt"));

        var result = await HandlerOver(world).HandleAsync(Command(FilesWorld.OwnAccountId), default);

        Assert.Equal("FilesAccountSuspended", result.Error!.Code);
        Assert.Empty(world.Agent.Calls);
    }
}
