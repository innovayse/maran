using Maran.Modules.Files.Commands.CompleteFileDownload;
using Maran.Modules.Files.Tests.TestSupport;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Files.Tests.Commands.CompleteFileDownload;

/// <summary>What the completion of a download records.</summary>
public sealed class CompleteFileDownloadCommandHandlerTests
{
    /// <summary>The entry names the account, the path and the bytes written.</summary>
    [Fact]
    public async Task The_entry_names_the_account_the_path_and_the_bytes_written()
    {
        var world = new FilesWorld();
        var handler = new CompleteFileDownloadCommandHandler(
            new StubAccountDirectory(FilesTestAccounts.Account(FilesWorld.OwnAccountId, "alice", AccountState.Active)),
            world.Journal);

        var result = await handler.HandleAsync(
            new CompleteFileDownloadCommand(
                FilesWorld.OwnAccountId, "docs/report.pdf", 1234, FilesWorld.Origin.IpAddress, FilesWorld.Origin.UserAgent),
            default);

        Assert.True(result.IsSuccess);
        var entry = Assert.Single(world.Writer.Entries);
        Assert.Equal(FileAuditActions.FileDownloadCompleted, entry.Action);
        Assert.True(entry.Succeeded);
        Assert.Equal("alice:docs/report.pdf (1234 bytes)", entry.Subject);
        Assert.Equal(FilesWorld.OwnAccountId, entry.AccountId);
    }

    /// <summary>An account the caller cannot see is not found and nothing is recorded under it.</summary>
    [Fact]
    public async Task An_account_the_caller_cannot_see_is_not_found_and_nothing_is_recorded()
    {
        var world = new FilesWorld();
        var handler = new CompleteFileDownloadCommandHandler(new StubAccountDirectory(), world.Journal);

        var result = await handler.HandleAsync(
            new CompleteFileDownloadCommand(FilesWorld.StrangerAccountId, "docs/report.pdf", 1), default);

        Assert.Equal("AccountNotFound", result.Error!.Code);
        Assert.Empty(world.Writer.Entries);
    }
}
