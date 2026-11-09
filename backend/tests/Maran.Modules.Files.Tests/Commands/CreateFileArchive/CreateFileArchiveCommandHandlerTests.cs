using Maran.Agent.Client.Interfaces;
using Maran.Modules.Files.Commands.CreateFileArchive;
using Maran.Modules.Files.Domain.Enums;
using Maran.Modules.Files.Options;
using Maran.Modules.Files.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maran.Modules.Files.Tests.Commands.CreateFileArchive;

/// <summary>
/// Holds <see cref="CreateFileArchiveCommandHandler"/> to archiving only inside the caller's own account,
/// refusing a source list over the batch ceiling before the agent is asked, translating the agent's refusals
/// and auditing the destination.
/// </summary>
public sealed class CreateFileArchiveCommandHandlerTests
{
    /// <summary>Builds the handler over a world's collaborators.</summary>
    /// <param name="world">The world.</param>
    /// <returns>The handler.</returns>
    private static CreateFileArchiveCommandHandler HandlerOf(FilesHandlerWorld world)
    {
        return new CreateFileArchiveCommandHandler(
            world.Gate,
            world.Client,
            world.Journal,
            world.Monitor,
            NullLogger<CreateFileArchiveCommandHandler>.Instance);
    }

    /// <summary>Builds an archive command.</summary>
    /// <param name="accountId">The account.</param>
    /// <param name="sources">The paths to archive.</param>
    /// <returns>The command.</returns>
    private static CreateFileArchiveCommand CommandOf(Guid accountId, params string[] sources)
    {
        return new CreateFileArchiveCommand(
            accountId,
            sources,
            "backups/site.zip",
            ArchiveFormatKind.Zip,
            FilesHandlerWorld.Address,
            FilesHandlerWorld.UserAgent);
    }

    /// <summary>The archive's size comes from the agent and the destination is audited.</summary>
    [Fact]
    public async Task Archiving_reports_the_agents_size_and_audits_the_destination()
    {
        var world = FilesHandlerWorld.ForCustomer();
        world.Client.ArchiveBytes = 4096;

        var result = await HandlerOf(world).HandleAsync(CommandOf(world.Active.Id, "public_html", "notes.txt"), default);

        Assert.Equal(4096UL, result.Value.SizeBytes);
        var call = Assert.Single(world.Client.Calls);
        Assert.Equal(nameof(IAgentFilesClient.CreateArchiveAsync), call.Operation);
        Assert.Equal("alpha", call.Account);
        Assert.Equal("backups/site.zip", call.Path);
        var entry = Assert.Single(world.Writer.Entries);
        Assert.Equal(FileAuditActions.FileArchived, entry.Action);
        Assert.Equal("alpha:backups/site.zip", entry.Subject);
        Assert.True(entry.Succeeded);
    }

    /// <summary>A source list over the batch ceiling is refused before the agent is asked.</summary>
    [Fact]
    public async Task A_source_list_over_the_ceiling_is_refused_before_the_agent_is_asked()
    {
        var world = FilesHandlerWorld.ForCustomer(new FilesOptions { MaxBatchItems = 2 });

        var result = await HandlerOf(world).HandleAsync(CommandOf(world.Active.Id, "a", "b", "c"), default);

        Assert.Equal("FilesBatchTooLarge", result.Error!.Code);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
        Assert.Empty(world.Client.Calls);
        Assert.False(Assert.Single(world.Writer.Entries).Succeeded);
    }

    /// <summary>An archive the agent judges unsafe is a refusal that names no entry, audited as a failure.</summary>
    [Fact]
    public async Task An_archive_the_agent_refuses_is_a_refusal_and_is_audited_as_a_failure()
    {
        var world = FilesHandlerWorld.ForCustomer();
        world.Client.FailOn(
            nameof(IAgentFilesClient.CreateArchiveAsync),
            string.Empty,
            Error.Of("AgentArchiveUnsafe", ErrorType.Validation));

        var result = await HandlerOf(world).HandleAsync(CommandOf(world.Active.Id, "a"), default);

        Assert.Equal("FilesArchiveRefused", result.Error!.Code);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
        Assert.False(Assert.Single(world.Writer.Entries).Succeeded);
    }

    /// <summary>A full quota is a conflict the customer can act on.</summary>
    [Fact]
    public async Task A_full_quota_is_reported_as_a_quota_conflict()
    {
        var world = FilesHandlerWorld.ForCustomer();
        world.Client.FailOn(
            nameof(IAgentFilesClient.CreateArchiveAsync),
            string.Empty,
            Error.Of("AgentQuotaExceeded", ErrorType.Conflict));

        var result = await HandlerOf(world).HandleAsync(CommandOf(world.Active.Id, "a"), default);

        Assert.Equal("FilesQuotaExceeded", result.Error!.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
    }

    /// <summary>Another tenant's account is not found and the agent is never asked.</summary>
    [Fact]
    public async Task Another_tenants_account_is_not_found_and_the_agent_is_never_asked()
    {
        var world = FilesHandlerWorld.ForCustomer();

        var result = await HandlerOf(world).HandleAsync(CommandOf(world.Stranger, "a"), default);

        Assert.Equal("AccountNotFound", result.Error!.Code);
        Assert.Empty(world.Client.Calls);
    }

    /// <summary>A suspended account refuses a customer and admits an administrator.</summary>
    [Fact]
    public async Task A_suspended_account_refuses_a_customer_and_admits_an_administrator()
    {
        var customer = FilesHandlerWorld.ForCustomer();
        var administrator = FilesHandlerWorld.ForAdministrator();

        var refused = await HandlerOf(customer).HandleAsync(CommandOf(customer.Suspended.Id, "a"), default);
        var admitted = await HandlerOf(administrator).HandleAsync(CommandOf(administrator.Suspended.Id, "a"), default);

        Assert.Equal("FilesAccountSuspended", refused.Error!.Code);
        Assert.Empty(customer.Client.Calls);
        Assert.True(admitted.IsSuccess);
    }
}
