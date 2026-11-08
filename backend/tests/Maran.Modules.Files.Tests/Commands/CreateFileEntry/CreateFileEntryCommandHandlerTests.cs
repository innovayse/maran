using Maran.Agent.Client.Interfaces;
using Maran.Modules.Files.Commands.CreateFileEntry;
using Maran.Modules.Files.Domain.Enums;
using Maran.Modules.Files.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maran.Modules.Files.Tests.Commands.CreateFileEntry;

/// <summary>
/// Holds <see cref="CreateFileEntryCommandHandler"/> to creating a file or a folder with the module's default
/// mode, reading the new entry back, answering a clash as a conflict, and auditing the outcome.
/// </summary>
public sealed class CreateFileEntryCommandHandlerTests
{
    /// <summary>Builds the handler over a world's collaborators.</summary>
    /// <param name="world">The world.</param>
    /// <returns>The handler.</returns>
    private static CreateFileEntryCommandHandler HandlerOf(FilesHandlerWorld world)
    {
        return new CreateFileEntryCommandHandler(
            world.Gate, world.Client, world.Reader, world.Journal, NullLogger<CreateFileEntryCommandHandler>.Instance);
    }

    /// <summary>Builds a create command.</summary>
    /// <param name="accountId">The account.</param>
    /// <param name="path">The path to create.</param>
    /// <param name="kind">File or folder.</param>
    /// <returns>The command.</returns>
    private static CreateFileEntryCommand CommandOf(Guid accountId, string path, NewFileEntryKind kind)
    {
        return new CreateFileEntryCommand(
            accountId, path, kind, FilesHandlerWorld.Address, FilesHandlerWorld.UserAgent);
    }

    /// <summary>A new file is created with the default file mode, read back, and audited.</summary>
    [Fact]
    public async Task Creating_a_file_asks_the_agent_for_mode_644_reads_it_back_and_audits_it()
    {
        var world = FilesHandlerWorld.ForCustomer();
        world.Client.AddFile("", "new.txt", 0, 0b110_100_100, DateTimeOffset.UnixEpoch);

        var result = await HandlerOf(world).HandleAsync(
            CommandOf(world.Active.Id, "new.txt", NewFileEntryKind.File), default);

        Assert.True(result.IsSuccess);
        Assert.Equal("new.txt", result.Value.Path);
        Assert.Equal("644", result.Value.Mode);
        Assert.Equal(FileEntryKind.File, result.Value.Kind);
        var created = world.Client.Calls.Single(call =>
        {
            return call.Operation == nameof(IAgentFilesClient.CreateFileAsync);
        });
        Assert.Equal("alpha", created.Account);
        Assert.Equal(0b110_100_100u, created.Mode);
        var entry = Assert.Single(world.Writer.Entries);
        Assert.Equal(FileAuditActions.FileCreated, entry.Action);
        Assert.Equal("alpha:new.txt", entry.Subject);
        Assert.True(entry.Succeeded);
    }

    /// <summary>A new folder is created with the default folder mode.</summary>
    [Fact]
    public async Task Creating_a_folder_asks_the_agent_for_mode_755()
    {
        var world = FilesHandlerWorld.ForCustomer();
        world.Client.AddDirectory("docs", "reports");

        var result = await HandlerOf(world).HandleAsync(
            CommandOf(world.Active.Id, "docs/reports", NewFileEntryKind.Directory), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(FileEntryKind.Directory, result.Value.Kind);
        var created = world.Client.Calls.Single(call =>
        {
            return call.Operation == nameof(IAgentFilesClient.CreateDirectoryAsync);
        });
        Assert.Equal("docs/reports", created.Path);
        Assert.Equal(0b111_101_101u, created.Mode);
    }

    /// <summary>A name already taken is a conflict, and the failure is audited.</summary>
    [Fact]
    public async Task A_name_already_taken_is_a_conflict_and_the_failure_is_audited()
    {
        var world = FilesHandlerWorld.ForCustomer();
        world.Client.FailOn(
            nameof(IAgentFilesClient.CreateFileAsync), string.Empty, Error.Of("AgentAlreadyExists", ErrorType.Conflict));

        var result = await HandlerOf(world).HandleAsync(
            CommandOf(world.Active.Id, "taken.txt", NewFileEntryKind.File), default);

        Assert.Equal("FilesEntryExists", result.Error!.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        var entry = Assert.Single(world.Writer.Entries);
        Assert.False(entry.Succeeded);
        Assert.Equal("alpha:taken.txt", entry.Subject);
    }

    /// <summary>A full quota is a conflict the customer can act on.</summary>
    [Fact]
    public async Task A_full_quota_is_reported_as_a_quota_conflict()
    {
        var world = FilesHandlerWorld.ForCustomer();
        world.Client.FailOn(
            nameof(IAgentFilesClient.CreateFileAsync), string.Empty, Error.Of("AgentQuotaExceeded", ErrorType.Conflict));

        var result = await HandlerOf(world).HandleAsync(
            CommandOf(world.Active.Id, "big.bin", NewFileEntryKind.File), default);

        Assert.Equal("FilesQuotaExceeded", result.Error!.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
    }

    /// <summary>Another tenant's account is not found and the agent is never asked.</summary>
    [Fact]
    public async Task Another_tenants_account_is_not_found_and_the_agent_is_never_asked()
    {
        var world = FilesHandlerWorld.ForCustomer();

        var result = await HandlerOf(world).HandleAsync(
            CommandOf(world.Stranger, "new.txt", NewFileEntryKind.File), default);

        Assert.Equal("AccountNotFound", result.Error!.Code);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        Assert.Empty(world.Client.Calls);
    }

    /// <summary>A customer on a suspended account is refused before the agent is asked.</summary>
    [Fact]
    public async Task A_customer_on_a_suspended_account_is_refused()
    {
        var world = FilesHandlerWorld.ForCustomer();

        var result = await HandlerOf(world).HandleAsync(
            CommandOf(world.Suspended.Id, "new.txt", NewFileEntryKind.File), default);

        Assert.Equal("FilesAccountSuspended", result.Error!.Code);
        Assert.Empty(world.Client.Calls);
    }

    /// <summary>An administrator may create on a suspended account.</summary>
    [Fact]
    public async Task An_administrator_may_create_on_a_suspended_account()
    {
        var world = FilesHandlerWorld.ForAdministrator();
        world.Client.AddFile("", "new.txt", 0, 0b110_100_100, DateTimeOffset.UnixEpoch);

        var result = await HandlerOf(world).HandleAsync(
            CommandOf(world.Suspended.Id, "new.txt", NewFileEntryKind.File), default);

        Assert.True(result.IsSuccess);
        Assert.All(world.Client.Calls, call =>
        {
            Assert.Equal("beta", call.Account);
        });
    }
}
