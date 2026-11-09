using Maran.Modules.Files.Domain.Enums;
using Maran.Modules.Files.Queries.GetFileInfo;
using Maran.Modules.Files.Tests.TestSupport;

namespace Maran.Modules.Files.Tests.Queries.GetFileInfo;

/// <summary>What asking about one entry does, and how it finds the entry among a folder's thousands.</summary>
public sealed class GetFileInfoQueryHandlerTests
{
    /// <summary>Builds the handler over a world.</summary>
    /// <param name="world">The world the handler reads.</param>
    /// <returns>The handler.</returns>
    private static GetFileInfoQueryHandler HandlerOver(FilesWorld world)
    {
        return new GetFileInfoQueryHandler(world.Gate, world.Lookup, world.Names);
    }

    /// <summary>A query as the action stamps it.</summary>
    /// <param name="accountId">The account asked about.</param>
    /// <param name="path">The entry.</param>
    /// <returns>The query.</returns>
    private static GetFileInfoQuery Query(Guid accountId, string path)
    {
        return new GetFileInfoQuery(accountId, path, FilesWorld.Origin.IpAddress, FilesWorld.Origin.UserAgent);
    }

    /// <summary>Another tenant's account is not found and the agent is never asked.</summary>
    [Fact]
    public async Task Another_tenants_account_is_not_found_and_the_agent_sees_nothing()
    {
        var world = new FilesWorld();
        world.Agent.Put("docs", FilesWorld.File("a.txt"));

        var result = await HandlerOver(world).HandleAsync(Query(FilesWorld.StrangerAccountId, "docs/a.txt"), default);

        Assert.Equal("AccountNotFound", result.Error!.Code);
        Assert.Empty(world.Agent.Calls);
    }

    /// <summary>The control: the caller's own entry is described, with its mode in octal digits.</summary>
    [Fact]
    public async Task The_callers_own_entry_is_described()
    {
        var world = new FilesWorld();
        world.Agent.Put("docs", FilesWorld.File("a.txt", 42, 0b110_100_000));

        var result = await HandlerOver(world).HandleAsync(Query(FilesWorld.OwnAccountId, "docs/a.txt"), default);

        Assert.True(result.IsSuccess);
        Assert.Equal("a.txt", result.Value.Name);
        Assert.Equal("docs/a.txt", result.Value.Path);
        Assert.Equal(42ul, result.Value.SizeBytes);
        Assert.Equal("640", result.Value.Mode);
        Assert.Equal(FileEntryKind.File, result.Value.Kind);
    }

    /// <summary>An entry a later page of a large folder holds is found by reading on, and a hidden one is found too.</summary>
    [Fact]
    public async Task An_entry_on_a_later_page_of_a_large_folder_is_found()
    {
        var world = new FilesWorld();
        for (var index = 0; index < 2500; index++)
        {
            world.Agent.Put("big", FilesWorld.File($"f{index:0000}"));
        }

        world.Agent.Put("big", FilesWorld.File(".zzz-hidden"));

        var late = await HandlerOver(world).HandleAsync(Query(FilesWorld.OwnAccountId, "big/f2499"), default);
        var hidden = await HandlerOver(world).HandleAsync(Query(FilesWorld.OwnAccountId, "big/.zzz-hidden"), default);

        Assert.True(late.IsSuccess);
        Assert.True(hidden.IsSuccess);

        // f2499 is the last of 2501 names, so it takes three pages of a thousand; the dot-file sorts first
        // and is on the first page of its own lookup.
        Assert.Equal(4, world.Agent.ListRequests.Count);
        Assert.Contains(world.Agent.ListRequests, listing => { return listing.Offset == 2000; });
    }

    /// <summary>A name the folder does not hold is the module's not-found, after the whole folder was read.</summary>
    [Fact]
    public async Task A_name_the_folder_does_not_hold_is_not_found()
    {
        var world = new FilesWorld();
        world.Agent.Put("docs", FilesWorld.File("a.txt"));

        var result = await HandlerOver(world).HandleAsync(Query(FilesWorld.OwnAccountId, "docs/b.txt"), default);

        Assert.Equal("FilesEntryNotFound", result.Error!.Code);
        Assert.Single(world.Agent.ListRequests);
    }

    /// <summary>A folder is described as a folder, with its kind named by the module.</summary>
    [Fact]
    public async Task A_folder_is_described_as_a_folder()
    {
        var world = new FilesWorld();
        world.Agent.Put(string.Empty, FilesWorld.Folder("docs"));

        var result = await HandlerOver(world).HandleAsync(Query(FilesWorld.OwnAccountId, "docs"), default);

        Assert.Equal(FileEntryKind.Directory, result.Value.Kind);
        Assert.Equal("FileEntryKindDirectory", result.Value.KindName);
    }
}
