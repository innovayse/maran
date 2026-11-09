using Maran.Modules.Files.Commands.AbortFileDownload;
using Maran.Modules.Files.Tests.TestSupport;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Files.Tests.Commands.AbortFileDownload;

/// <summary>What a download that ended before its last byte records.</summary>
public sealed class AbortFileDownloadCommandHandlerTests
{
    /// <summary>The entry is a failure that names the account and the path, and carries the caller's origin.</summary>
    [Fact]
    public async Task The_entry_is_a_failure_naming_the_account_and_the_path()
    {
        var world = new FilesWorld();
        var handler = new AbortFileDownloadCommandHandler(
            new StubAccountDirectory(FilesTestAccounts.Account(FilesWorld.OwnAccountId, "alice", AccountState.Active)),
            world.Journal);

        var result = await handler.HandleAsync(
            new AbortFileDownloadCommand(
                FilesWorld.OwnAccountId, "docs/report.pdf", FilesWorld.Origin.IpAddress, FilesWorld.Origin.UserAgent),
            default);

        Assert.True(result.IsSuccess);
        var entry = Assert.Single(world.Writer.Entries);
        Assert.Equal(FileAuditActions.FileDownloadAborted, entry.Action);
        Assert.False(entry.Succeeded);
        Assert.Equal("alice:docs/report.pdf", entry.Subject);
        Assert.Equal(FilesWorld.OwnAccountId, entry.AccountId);
    }

    /// <summary>An account the caller cannot see is not found and nothing is recorded under it.</summary>
    [Fact]
    public async Task An_account_the_caller_cannot_see_is_not_found_and_nothing_is_recorded()
    {
        var world = new FilesWorld();
        var handler = new AbortFileDownloadCommandHandler(new StubAccountDirectory(), world.Journal);

        var result = await handler.HandleAsync(
            new AbortFileDownloadCommand(FilesWorld.StrangerAccountId, "docs/report.pdf"), default);

        Assert.Equal("AccountNotFound", result.Error!.Code);
        Assert.Empty(world.Writer.Entries);
    }
}
