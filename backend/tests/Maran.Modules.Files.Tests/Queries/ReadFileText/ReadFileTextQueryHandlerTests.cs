using System.Text;
using Maran.Modules.Files.Options;
using Maran.Modules.Files.Queries.ReadFileText;
using Maran.Modules.Files.Tests.TestSupport;
using Maran.SharedKernel.Results;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maran.Modules.Files.Tests.Queries.ReadFileText;

/// <summary>What reading a file for the editor returns, and which files it refuses to hand to a text box.</summary>
public sealed class ReadFileTextQueryHandlerTests
{
    /// <summary>Builds the handler over a world.</summary>
    /// <param name="world">The world the handler reads.</param>
    /// <returns>The handler.</returns>
    private static ReadFileTextQueryHandler HandlerOver(FilesWorld world)
    {
        return new ReadFileTextQueryHandler(
            world.Gate, world.Lookup, world.Agent, world.Options, NullLogger<ReadFileTextQueryHandler>.Instance);
    }

    /// <summary>A query as the action stamps it.</summary>
    /// <param name="accountId">The account asked about.</param>
    /// <param name="path">The file.</param>
    /// <returns>The query.</returns>
    private static ReadFileTextQuery Query(Guid accountId, string path = "notes.txt")
    {
        return new ReadFileTextQuery(accountId, path, FilesWorld.Origin.IpAddress, FilesWorld.Origin.UserAgent);
    }

    /// <summary>Puts a regular file with this content in the home.</summary>
    /// <param name="world">The world to put it in.</param>
    /// <param name="content">The file's bytes.</param>
    private static void PutNotes(FilesWorld world, byte[] content)
    {
        world.Agent.Put(string.Empty, FilesWorld.File("notes.txt", (ulong)content.Length, 0b110_100_000), content);
    }

    /// <summary>Another tenant's account is not found and the agent is never asked, not even for a listing.</summary>
    [Fact]
    public async Task Another_tenants_account_is_not_found_and_the_agent_sees_nothing()
    {
        var world = new FilesWorld();
        PutNotes(world, "hello"u8.ToArray());

        var result = await HandlerOver(world).HandleAsync(Query(FilesWorld.StrangerAccountId), default);

        Assert.Equal("AccountNotFound", result.Error!.Code);
        Assert.Empty(world.Agent.Calls);
    }

    /// <summary>The control: the caller's own text file is returned with its modification time and mode.</summary>
    [Fact]
    public async Task The_callers_own_text_file_is_returned_with_its_time_and_mode()
    {
        var world = new FilesWorld();
        PutNotes(world, Encoding.UTF8.GetBytes("héllo\r\nworld"));

        var result = await HandlerOver(world).HandleAsync(Query(FilesWorld.OwnAccountId), default);

        Assert.True(result.IsSuccess);
        Assert.Equal("héllo\r\nworld", result.Value.Content);
        Assert.Equal(FilesWorld.Modified, result.Value.ModifiedAt);
        Assert.Equal("640", result.Value.Mode);
        Assert.Equal((ulong)Encoding.UTF8.GetByteCount("héllo\r\nworld"), result.Value.SizeBytes);
    }

    /// <summary>A file holding a NUL byte is not text the editor can hold.</summary>
    [Fact]
    public async Task A_file_holding_a_nul_byte_is_not_editable()
    {
        var world = new FilesWorld();
        PutNotes(world, [0x41, 0x00, 0x42]);

        var result = await HandlerOver(world).HandleAsync(Query(FilesWorld.OwnAccountId), default);

        Assert.Equal("FilesNotEditable", result.Error!.Code);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
    }

    /// <summary>A file that is not valid UTF-8 is not editable: a save would silently rewrite its bytes.</summary>
    [Fact]
    public async Task A_file_that_is_not_valid_utf8_is_not_editable()
    {
        var world = new FilesWorld();
        PutNotes(world, [0xC3, 0x28]);

        var result = await HandlerOver(world).HandleAsync(Query(FilesWorld.OwnAccountId), default);

        Assert.Equal("FilesNotEditable", result.Error!.Code);
    }

    /// <summary>A file over the edit cap is refused from the listing, without being read.</summary>
    [Fact]
    public async Task A_file_over_the_edit_cap_is_refused_without_being_read()
    {
        var world = new FilesWorld(options: new FilesOptions { MaxEditBytes = 8 });
        PutNotes(world, "123456789"u8.ToArray());

        var result = await HandlerOver(world).HandleAsync(Query(FilesWorld.OwnAccountId), default);

        Assert.Equal("FilesNotEditable", result.Error!.Code);
        Assert.DoesNotContain(world.Agent.Calls, call =>
        {
            return call.Operation == nameof(Maran.Agent.Client.Interfaces.IAgentFilesClient.ReadFileAsync);
        });
    }

    /// <summary>A file exactly at the cap is editable: the cap is a maximum, not an exclusive bound.</summary>
    [Fact]
    public async Task A_file_exactly_at_the_edit_cap_is_editable()
    {
        var world = new FilesWorld(options: new FilesOptions { MaxEditBytes = 8 });
        PutNotes(world, "12345678"u8.ToArray());

        var result = await HandlerOver(world).HandleAsync(Query(FilesWorld.OwnAccountId), default);

        Assert.True(result.IsSuccess);
    }

    /// <summary>A folder is not a file the editor can open.</summary>
    [Fact]
    public async Task A_folder_is_not_editable()
    {
        var world = new FilesWorld();
        world.Agent.Put(string.Empty, FilesWorld.Folder("notes.txt"));

        var result = await HandlerOver(world).HandleAsync(Query(FilesWorld.OwnAccountId), default);

        Assert.Equal("FilesNotEditable", result.Error!.Code);
    }

    /// <summary>A file that is not there is the module's not-found.</summary>
    [Fact]
    public async Task A_missing_file_is_not_found()
    {
        var world = new FilesWorld();
        world.Agent.Put(string.Empty, FilesWorld.File("other.txt"));

        var result = await HandlerOver(world).HandleAsync(Query(FilesWorld.OwnAccountId), default);

        Assert.Equal("FilesEntryNotFound", result.Error!.Code);
    }
}
