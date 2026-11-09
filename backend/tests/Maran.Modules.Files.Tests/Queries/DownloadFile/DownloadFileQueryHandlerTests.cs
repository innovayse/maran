using Maran.Modules.Files.Options;
using Maran.Modules.Files.Queries.DownloadFile;
using Maran.Modules.Files.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maran.Modules.Files.Tests.Queries.DownloadFile;

/// <summary>What opening a download refuses before a byte is sent, and what the download it hands back owes.</summary>
public sealed class DownloadFileQueryHandlerTests
{
    /// <summary>Builds the handler over a world.</summary>
    /// <param name="world">The world the handler reads.</param>
    /// <returns>The handler.</returns>
    private static DownloadFileQueryHandler HandlerOver(FilesWorld world)
    {
        return new DownloadFileQueryHandler(
            world.Gate,
            world.Lookup,
            world.Agent,
            world.Slots,
            world.Journal,
            world.Options,
            NullLogger<DownloadFileQueryHandler>.Instance);
    }

    /// <summary>A query as the action stamps it.</summary>
    /// <param name="accountId">The account asked about.</param>
    /// <param name="path">The file.</param>
    /// <returns>The query.</returns>
    private static DownloadFileQuery Query(Guid accountId, string path = "report.pdf")
    {
        return new DownloadFileQuery(accountId, path, FilesWorld.Origin.IpAddress, FilesWorld.Origin.UserAgent);
    }

    /// <summary>Another tenant's account is not found, the agent is never asked and no slot is taken.</summary>
    [Fact]
    public async Task Another_tenants_account_is_not_found_and_the_agent_sees_nothing()
    {
        var world = new FilesWorld();
        world.Agent.Put(string.Empty, FilesWorld.File("report.pdf", 5), "12345"u8.ToArray());

        var result = await HandlerOver(world).HandleAsync(Query(FilesWorld.StrangerAccountId), default);

        Assert.Equal("AccountNotFound", result.Error!.Code);
        Assert.Empty(world.Agent.Calls);
    }

    /// <summary>The control: the caller's own file streams its bytes, under a cleaned name, and the opening is journalled.</summary>
    [Fact]
    public async Task The_callers_own_file_streams_its_bytes_and_the_opening_is_journalled()
    {
        var world = new FilesWorld();
        world.Agent.Put(string.Empty, FilesWorld.File("report.pdf", 5), "12345"u8.ToArray());

        var result = await HandlerOver(world).HandleAsync(Query(FilesWorld.OwnAccountId), default);

        Assert.True(result.IsSuccess);
        await using var download = result.Value;
        using var body = new MemoryStream();
        await download.CopyToAsync(body, default);
        Assert.Equal("12345"u8.ToArray(), body.ToArray());
        Assert.Equal("report.pdf", download.FileName);
        Assert.Equal(5ul, download.SizeBytes);
        var entry = Assert.Single(world.Writer.Entries);
        Assert.Equal(FileAuditActions.FileDownloadStarted, entry.Action);
        Assert.True(entry.Succeeded);
        Assert.Equal("alice:report.pdf", entry.Subject);
    }

    /// <summary>A file over the download cap is refused before it is opened, with no entry streamed.</summary>
    [Fact]
    public async Task A_file_over_the_download_cap_is_refused_before_it_is_opened()
    {
        var world = new FilesWorld(options: new FilesOptions { MaxDownloadBytes = 4 });
        world.Agent.Put(string.Empty, FilesWorld.File("report.pdf", 5), "12345"u8.ToArray());

        var result = await HandlerOver(world).HandleAsync(Query(FilesWorld.OwnAccountId), default);

        Assert.Equal("FilesDownloadTooLarge", result.Error!.Code);
        Assert.DoesNotContain(world.Agent.Calls, call =>
        {
            return call.Operation == nameof(Maran.Agent.Client.Interfaces.IAgentFilesClient.ReadFileAsync);
        });
        Assert.False(Assert.Single(world.Writer.Entries).Succeeded);
    }

    /// <summary>A folder cannot be downloaded: the path names a file, and there is none.</summary>
    [Fact]
    public async Task A_folder_is_not_found_as_a_download()
    {
        var world = new FilesWorld();
        world.Agent.Put(string.Empty, FilesWorld.Folder("report.pdf"));

        var result = await HandlerOver(world).HandleAsync(Query(FilesWorld.OwnAccountId), default);

        Assert.Equal("FilesEntryNotFound", result.Error!.Code);
    }

    /// <summary>A second download past the account's bound is refused at once, and a finished one gives its slot back.</summary>
    [Fact]
    public async Task A_download_past_the_accounts_bound_is_refused_and_a_finished_one_frees_its_slot()
    {
        var world = new FilesWorld(options: new FilesOptions { MaxConcurrentTransfersPerAccount = 1 });
        world.Agent.Put(string.Empty, FilesWorld.File("report.pdf", 5), "12345"u8.ToArray());
        var handler = HandlerOver(world);

        var first = await handler.HandleAsync(Query(FilesWorld.OwnAccountId), default);
        var second = await handler.HandleAsync(Query(FilesWorld.OwnAccountId), default);

        Assert.True(first.IsSuccess);
        Assert.Equal("FilesAccountBusy", second.Error!.Code);
        Assert.Equal(ErrorType.Conflict, second.Error.Type);
        Assert.Equal(1, world.Agent.Calls.Count(call => { return call.Operation == "ReadFileAsync"; }));

        await first.Value.DisposeAsync();
        var third = await handler.HandleAsync(Query(FilesWorld.OwnAccountId), default);
        Assert.True(third.IsSuccess);
        await third.Value.DisposeAsync();
    }

    /// <summary>A refusal after the slot was taken gives the slot back.</summary>
    [Fact]
    public async Task A_refusal_after_the_slot_was_taken_gives_the_slot_back()
    {
        var world = new FilesWorld(options: new FilesOptions { MaxConcurrentTransfersPerAccount = 1 });
        var handler = HandlerOver(world);

        var missing = await handler.HandleAsync(Query(FilesWorld.OwnAccountId, "nothing.pdf"), default);
        world.Agent.Put(string.Empty, FilesWorld.File("report.pdf", 5), "12345"u8.ToArray());
        var present = await handler.HandleAsync(Query(FilesWorld.OwnAccountId), default);

        Assert.False(missing.IsSuccess);
        Assert.True(present.IsSuccess);
        await present.Value.DisposeAsync();
    }

    /// <summary>A stream that ends short of the length the response declared is an error, never a quiet finish.</summary>
    [Fact]
    public async Task A_stream_that_ends_short_of_the_declared_length_is_an_error()
    {
        var world = new FilesWorld();
        world.Agent.Put(string.Empty, FilesWorld.File("report.pdf", 5), "12345"u8.ToArray());
        world.Agent.ReadsDeliverOnly = 3;

        var result = await HandlerOver(world).HandleAsync(Query(FilesWorld.OwnAccountId), default);

        await using var download = result.Value;
        await Assert.ThrowsAsync<IOException>(async () => { await download.CopyToAsync(new MemoryStream(), default); });
    }

    /// <summary>A stream the agent ends with a refusal half way is an error, never a quiet finish.</summary>
    [Fact]
    public async Task A_stream_the_agent_refuses_half_way_is_an_error()
    {
        var world = new FilesWorld();
        world.Agent.Put(string.Empty, FilesWorld.File("report.pdf", 8), "12345678"u8.ToArray());
        world.Agent.ReadsFailHalfWay = true;

        var result = await HandlerOver(world).HandleAsync(Query(FilesWorld.OwnAccountId), default);

        await using var download = result.Value;
        await Assert.ThrowsAsync<IOException>(async () => { await download.CopyToAsync(new MemoryStream(), default); });
    }

    /// <summary>A suspended account's customer is refused before a slot is taken or the agent asked.</summary>
    [Fact]
    public async Task A_suspended_account_is_refused_to_its_customer()
    {
        var world = new FilesWorld(AccountState.Suspended);
        world.Agent.Put(string.Empty, FilesWorld.File("report.pdf", 5), "12345"u8.ToArray());

        var result = await HandlerOver(world).HandleAsync(Query(FilesWorld.OwnAccountId), default);

        Assert.Equal("FilesAccountSuspended", result.Error!.Code);
        Assert.Empty(world.Agent.Calls);
    }
}
