using Maran.Agent.Client.Interfaces;
using Maran.Modules.Files.Commands.ChangeFilePermissions;
using Maran.Modules.Files.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maran.Modules.Files.Tests.Commands.ChangeFilePermissions;

/// <summary>
/// Holds <see cref="ChangeFilePermissionsCommandHandler"/> to changing the mode of one named path, refusing
/// a mode that is not three octal digits before the agent is asked, and auditing the change.
/// </summary>
public sealed class ChangeFilePermissionsCommandHandlerTests
{
    /// <summary>Builds the handler over a world's collaborators.</summary>
    /// <param name="world">The world.</param>
    /// <returns>The handler.</returns>
    private static ChangeFilePermissionsCommandHandler HandlerOf(FilesHandlerWorld world)
    {
        return new ChangeFilePermissionsCommandHandler(
            world.Gate, world.Runner, world.Client, NullLogger<ChangeFilePermissionsCommandHandler>.Instance);
    }

    /// <summary>Builds a permissions command.</summary>
    /// <param name="accountId">The account.</param>
    /// <param name="mode">The octal mode text.</param>
    /// <param name="recursive">Whether the change descends.</param>
    /// <param name="directoryMode">The folders' mode text, or null for the default.</param>
    /// <returns>The command.</returns>
    private static ChangeFilePermissionsCommand CommandOf(
        Guid accountId,
        string mode,
        bool recursive = false,
        string? directoryMode = null)
    {
        return new ChangeFilePermissionsCommand(
            accountId,
            "docs/a.txt",
            mode,
            recursive,
            directoryMode,
            FilesHandlerWorld.Address,
            FilesHandlerWorld.UserAgent);
    }

    /// <summary>A valid mode reaches the agent as the parsed bits and the change is audited.</summary>
    [Fact]
    public async Task A_valid_mode_reaches_the_agent_as_parsed_bits_and_is_audited()
    {
        var world = FilesHandlerWorld.ForCustomer();

        var result = await HandlerOf(world).HandleAsync(CommandOf(world.Active.Id, "640", recursive: true), default);

        Assert.True(result.IsSuccess);
        Assert.True(Assert.Single(result.Value.Items).Succeeded);
        var call = Assert.Single(world.Client.Calls);
        Assert.Equal(nameof(IAgentFilesClient.ChangePermissionsAsync), call.Operation);
        Assert.Equal("alpha", call.Account);
        Assert.Equal("docs/a.txt", call.Path);
        Assert.Equal(0b110_100_000u, call.Mode);
        var entry = Assert.Single(world.Writer.Entries);
        Assert.Equal(FileAuditActions.FilePermissionsChanged, entry.Action);
        Assert.Equal("alpha:docs/a.txt", entry.Subject);
    }

    /// <summary>A recursive change that names no folder mode gives folders the file mode with execute added.</summary>
    [Fact]
    public async Task A_recursive_change_with_no_folder_mode_gives_folders_execute_wherever_read_is_set()
    {
        var world = FilesHandlerWorld.ForCustomer();

        var result = await HandlerOf(world).HandleAsync(CommandOf(world.Active.Id, "644", recursive: true), default);

        Assert.True(result.IsSuccess);
        var change = Assert.Single(world.Client.PermissionChanges);
        Assert.Equal(0b110_100_100u, change.Mode);
        Assert.True(change.Recursive);
        Assert.Equal(0b111_101_101u, change.DirectoryMode);
    }

    /// <summary>A folder mode the caller names reaches the agent as sent, whatever the file mode is.</summary>
    [Fact]
    public async Task A_folder_mode_the_caller_names_reaches_the_agent_as_sent()
    {
        var world = FilesHandlerWorld.ForCustomer();

        var result = await HandlerOf(world)
            .HandleAsync(CommandOf(world.Active.Id, "644", recursive: true, directoryMode: "750"), default);

        Assert.True(result.IsSuccess);
        var change = Assert.Single(world.Client.PermissionChanges);
        Assert.Equal(0b110_100_100u, change.Mode);
        Assert.Equal(0b111_101_000u, change.DirectoryMode);
    }

    /// <summary>A folder mode that is not plain, non-zero permission bits never reaches the agent.</summary>
    /// <param name="directoryMode">The refused folder mode text.</param>
    [Theory]
    [InlineData("999")]
    [InlineData("4755")]
    [InlineData("000")]
    [InlineData("0")]
    [InlineData("")]
    public async Task A_folder_mode_that_is_not_plain_nonzero_permission_bits_never_reaches_the_agent(
        string directoryMode)
    {
        var world = FilesHandlerWorld.ForCustomer();

        var result = await HandlerOf(world)
            .HandleAsync(CommandOf(world.Active.Id, "644", recursive: true, directoryMode: directoryMode), default);

        Assert.Equal("FilesModeInvalid", result.Error!.Code);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
        Assert.Empty(world.Client.Calls);
    }

    /// <summary>A mode that is not plain octal permission bits never reaches the agent.</summary>
    /// <param name="mode">The refused mode text.</param>
    [Theory]
    [InlineData("999")]
    [InlineData("4755")]
    [InlineData("rwx")]
    [InlineData("")]
    public async Task A_mode_that_is_not_plain_permission_bits_never_reaches_the_agent(string mode)
    {
        var world = FilesHandlerWorld.ForCustomer();

        var result = await HandlerOf(world).HandleAsync(CommandOf(world.Active.Id, mode), default);

        Assert.Equal("FilesModeInvalid", result.Error!.Code);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
        Assert.Empty(world.Client.Calls);
    }

    /// <summary>An agent refusal is reported for the path and audited as a failure.</summary>
    [Fact]
    public async Task An_agent_refusal_is_reported_for_the_path_and_audited_as_a_failure()
    {
        var world = FilesHandlerWorld.ForCustomer();
        world.Client.FailOn(
            nameof(IAgentFilesClient.ChangePermissionsAsync), string.Empty, Error.Of("AgentNotFound", ErrorType.NotFound));

        var result = await HandlerOf(world).HandleAsync(CommandOf(world.Active.Id, "644"), default);

        var item = Assert.Single(result.Value.Items);
        Assert.False(item.Succeeded);
        Assert.Equal("FilesEntryNotFound", item.Code);
        Assert.False(Assert.Single(world.Writer.Entries).Succeeded);
    }

    /// <summary>Another tenant's account is not found and the agent is never asked.</summary>
    [Fact]
    public async Task Another_tenants_account_is_not_found_and_the_agent_is_never_asked()
    {
        var world = FilesHandlerWorld.ForCustomer();

        var result = await HandlerOf(world).HandleAsync(CommandOf(world.Stranger, "644"), default);

        Assert.Equal("AccountNotFound", result.Error!.Code);
        Assert.Empty(world.Client.Calls);
    }

    /// <summary>A customer on a suspended account is refused; an administrator is not.</summary>
    [Fact]
    public async Task A_suspended_account_refuses_a_customer_and_admits_an_administrator()
    {
        var customer = FilesHandlerWorld.ForCustomer();
        var administrator = FilesHandlerWorld.ForAdministrator();

        var refused = await HandlerOf(customer).HandleAsync(CommandOf(customer.Suspended.Id, "644"), default);
        var admitted = await HandlerOf(administrator).HandleAsync(CommandOf(administrator.Suspended.Id, "644"), default);

        Assert.Equal("FilesAccountSuspended", refused.Error!.Code);
        Assert.Empty(customer.Client.Calls);
        Assert.True(admitted.IsSuccess);
        Assert.Single(administrator.Client.Calls);
    }
}
