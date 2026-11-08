using Maran.Modules.Files.Commands.UploadFile;
using Maran.Modules.Files.Options;
using Maran.Modules.Files.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maran.Modules.Files.Tests.Commands.UploadFile;

/// <summary>What an upload forwards, refuses and records, and what it owes when the client goes away.</summary>
public sealed class UploadFileCommandHandlerTests
{
    /// <summary>Builds the handler over a world.</summary>
    /// <param name="world">The world the handler works in.</param>
    /// <returns>The handler.</returns>
    private static UploadFileCommandHandler HandlerOver(FilesWorld world)
    {
        return new UploadFileCommandHandler(
            world.Gate,
            world.Lookup,
            world.Agent,
            world.Slots,
            world.Journal,
            world.Options,
            NullLogger<UploadFileCommandHandler>.Instance);
    }

    /// <summary>A command as the action stamps it.</summary>
    /// <param name="accountId">The account asked about.</param>
    /// <param name="body">What the client sent.</param>
    /// <param name="overwrite">Whether an existing file may be replaced.</param>
    /// <param name="declared">The length the client declared, if any.</param>
    /// <returns>The command.</returns>
    private static UploadFileCommand Command(
        Guid accountId,
        byte[] body,
        bool overwrite = false,
        long? declared = null)
    {
        return new UploadFileCommand(
            accountId,
            "docs/report.pdf",
            overwrite,
            new MemoryStream(body),
            declared,
            FilesWorld.Origin.IpAddress,
            FilesWorld.Origin.UserAgent);
    }

    /// <summary>Another tenant's account is not found, nothing is read from the body and the refusal is journalled as an aborted upload.</summary>
    [Fact]
    public async Task Another_tenants_account_is_not_found_and_nothing_is_written()
    {
        var world = new FilesWorld();

        var result = await HandlerOver(world).HandleAsync(Command(FilesWorld.StrangerAccountId, [1, 2, 3]), default);

        Assert.Equal("AccountNotFound", result.Error!.Code);
        Assert.Empty(world.Agent.Calls);
        Assert.Equal(FileAuditActions.FileUploadAborted, Assert.Single(world.Writer.Entries).Action);
    }

    /// <summary>The control: a new file is written with the default mode, and the entries record the opening and the completion.</summary>
    [Fact]
    public async Task A_new_file_is_written_with_the_default_mode_and_both_entries_are_recorded()
    {
        var world = new FilesWorld();

        var result = await HandlerOver(world).HandleAsync(Command(FilesWorld.OwnAccountId, [1, 2, 3], declared: 3), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(3ul, result.Value.SizeBytes);
        var write = Assert.Single(world.Agent.StreamedWrites);
        Assert.Equal("docs/report.pdf", write.Path);
        Assert.Equal(0b110_100_100u, write.Mode);
        Assert.Equal(new byte[] { 1, 2, 3 }, write.Body);
        Assert.Equal((ulong)world.Limits.MaxUploadBytes, write.MaxBytes);
        Assert.Equal(
            [FileAuditActions.FileUploadStarted, FileAuditActions.FileUploaded],
            world.Writer.Entries.Select(entry => { return entry.Action; }));
        Assert.Equal("alice:docs/report.pdf (declared 3 bytes)", world.Writer.Entries[0].Subject);
        Assert.Equal("alice:docs/report.pdf (3 bytes)", world.Writer.Entries[1].Subject);
    }

    /// <summary>A declared length over the cap is refused before a slot is taken or anything is read.</summary>
    [Fact]
    public async Task A_declared_length_over_the_cap_is_refused_before_anything_is_read()
    {
        var world = new FilesWorld(options: new FilesOptions { MaxUploadBytes = 10 });

        var result = await HandlerOver(world).HandleAsync(
            Command(FilesWorld.OwnAccountId, [1], declared: 11), default);

        Assert.Equal("FilesUploadTooLarge", result.Error!.Code);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
        Assert.Empty(world.Agent.Calls);
    }

    /// <summary>The cap the agent is given is the configured one, whatever the client declared.</summary>
    [Fact]
    public async Task The_cap_the_agent_is_given_is_the_configured_one_whatever_was_declared()
    {
        var world = new FilesWorld(options: new FilesOptions { MaxUploadBytes = 10 });

        await HandlerOver(world).HandleAsync(Command(FilesWorld.OwnAccountId, [1], declared: 1), default);

        Assert.Equal(10ul, Assert.Single(world.Agent.StreamedWrites).MaxBytes);
    }

    /// <summary>An existing file is not replaced unless the caller said it may be.</summary>
    [Fact]
    public async Task An_existing_file_is_not_replaced_unless_the_caller_allowed_it()
    {
        var world = new FilesWorld();
        world.Agent.Put("docs", FilesWorld.File("report.pdf"));

        var refused = await HandlerOver(world).HandleAsync(Command(FilesWorld.OwnAccountId, [1]), default);

        Assert.Equal("FilesEntryExists", refused.Error!.Code);
        Assert.Equal(ErrorType.Conflict, refused.Error.Type);
        Assert.Empty(world.Agent.StreamedWrites);
    }

    /// <summary>A file that may be replaced is replaced with the mode it had.</summary>
    [Fact]
    public async Task A_replaced_file_keeps_the_mode_it_had()
    {
        var world = new FilesWorld();
        world.Agent.Put("docs", FilesWorld.File("report.pdf", 9, 0b110_110_000));

        var result = await HandlerOver(world).HandleAsync(Command(FilesWorld.OwnAccountId, [1], overwrite: true), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(0b110_110_000u, Assert.Single(world.Agent.StreamedWrites).Mode);
    }

    /// <summary>A folder at the path is never replaced, even when replacing is allowed.</summary>
    [Fact]
    public async Task A_folder_at_the_path_is_never_replaced()
    {
        var world = new FilesWorld();
        world.Agent.Put("docs", FilesWorld.Folder("report.pdf"));

        var result = await HandlerOver(world).HandleAsync(Command(FilesWorld.OwnAccountId, [1], overwrite: true), default);

        Assert.Equal("FilesEntryExists", result.Error!.Code);
        Assert.Empty(world.Agent.StreamedWrites);
    }

    /// <summary>A client that goes away mid-transfer leaves an aborted entry and no completion, and gives its slot back.</summary>
    [Fact]
    public async Task A_client_that_goes_away_leaves_an_aborted_entry_and_gives_the_slot_back()
    {
        var world = new FilesWorld(options: new FilesOptions { MaxConcurrentTransfersPerAccount = 1 });
        world.Agent.StreamedWriteThrows = new OperationCanceledException();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await HandlerOver(world).HandleAsync(Command(FilesWorld.OwnAccountId, [1]), cancellation.Token);
        });

        Assert.Equal(
            [FileAuditActions.FileUploadStarted, FileAuditActions.FileUploadAborted],
            world.Writer.Entries.Select(entry => { return entry.Action; }));
        Assert.DoesNotContain(world.Writer.Entries, entry => { return entry.Action == FileAuditActions.FileUploaded; });

        world.Agent.StreamedWriteThrows = null;
        var again = await HandlerOver(world).HandleAsync(Command(FilesWorld.OwnAccountId, [1]), default);
        Assert.True(again.IsSuccess);
    }

    /// <summary>A second upload past the account's bound is refused at once and nothing is written.</summary>
    [Fact]
    public async Task An_upload_past_the_accounts_bound_is_refused()
    {
        var world = new FilesWorld(options: new FilesOptions { MaxConcurrentTransfersPerAccount = 1 });
        var held = world.Slots.TryAcquire(FilesWorld.OwnAccountId);

        var result = await HandlerOver(world).HandleAsync(Command(FilesWorld.OwnAccountId, [1]), default);

        Assert.True(held.IsSuccess);
        Assert.Equal("FilesAccountBusy", result.Error!.Code);
        Assert.Empty(world.Agent.StreamedWrites);
        held.Value.Dispose();
    }

    /// <summary>An agent refusal is translated and journalled as an aborted upload.</summary>
    [Fact]
    public async Task An_agent_refusal_is_translated_and_journalled_as_an_aborted_upload()
    {
        var world = new FilesWorld();
        world.Agent.WriteRefusal = Error.Of("AgentQuotaExceeded", ErrorType.Failure);

        var result = await HandlerOver(world).HandleAsync(Command(FilesWorld.OwnAccountId, [1]), default);

        Assert.Equal("FilesQuotaExceeded", result.Error!.Code);
        Assert.Equal(FileAuditActions.FileUploadAborted, world.Writer.Entries[^1].Action);
        Assert.False(world.Writer.Entries[^1].Succeeded);
    }

    /// <summary>A customer on a suspended account is refused before the body is touched.</summary>
    [Fact]
    public async Task A_suspended_account_is_refused_to_its_customer()
    {
        var world = new FilesWorld(AccountState.Suspended);

        var result = await HandlerOver(world).HandleAsync(Command(FilesWorld.OwnAccountId, [1]), default);

        Assert.Equal("FilesAccountSuspended", result.Error!.Code);
        Assert.Empty(world.Agent.Calls);
    }
}
