using Maran.Agent.Client.Interfaces;
using Maran.Modules.Files.Commands.ExtractFileArchive;
using Maran.Modules.Files.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maran.Modules.Files.Tests.Commands.ExtractFileArchive;

/// <summary>
/// Holds <see cref="ExtractFileArchiveCommandHandler"/> to extracting only inside the caller's own account,
/// never asking the agent about a name whose format the module does not recognise, translating the agent's
/// refusals and auditing both ends of the extraction.
/// </summary>
public sealed class ExtractFileArchiveCommandHandlerTests
{
    /// <summary>Builds the handler over a world's collaborators.</summary>
    /// <param name="world">The world.</param>
    /// <returns>The handler.</returns>
    private static ExtractFileArchiveCommandHandler HandlerOf(FilesHandlerWorld world)
    {
        return new ExtractFileArchiveCommandHandler(
            world.Gate,
            world.Client,
            world.Journal,
            world.Monitor,
            NullLogger<ExtractFileArchiveCommandHandler>.Instance);
    }

    /// <summary>Builds an extraction command.</summary>
    /// <param name="accountId">The account.</param>
    /// <param name="archive">The archive path.</param>
    /// <returns>The command.</returns>
    private static ExtractFileArchiveCommand CommandOf(Guid accountId, string archive = "backups/site.zip")
    {
        return new ExtractFileArchiveCommand(
            accountId, archive, "restored", FilesHandlerWorld.Address, FilesHandlerWorld.UserAgent);
    }

    /// <summary>The entry count comes from the agent and both paths are audited.</summary>
    [Fact]
    public async Task Extracting_reports_the_agents_entry_count_and_audits_both_paths()
    {
        var world = FilesHandlerWorld.ForCustomer();
        world.Client.ExtractedEntries = 9;

        var result = await HandlerOf(world).HandleAsync(CommandOf(world.Active.Id), default);

        Assert.Equal(9, result.Value.EntryCount);
        var call = Assert.Single(world.Client.Calls);
        Assert.Equal(nameof(IAgentFilesClient.ExtractArchiveAsync), call.Operation);
        Assert.Equal("alpha", call.Account);
        Assert.Equal("backups/site.zip", call.Path);
        Assert.Equal("restored", call.Destination);
        var entry = Assert.Single(world.Writer.Entries);
        Assert.Equal(FileAuditActions.FileExtracted, entry.Action);
        Assert.Equal("alpha:backups/site.zip -> restored", entry.Subject);
        Assert.True(entry.Succeeded);
    }

    /// <summary>A name that is not an archive format never reaches the agent.</summary>
    [Fact]
    public async Task A_name_that_is_not_an_archive_format_never_reaches_the_agent()
    {
        var world = FilesHandlerWorld.ForCustomer();

        var result = await HandlerOf(world).HandleAsync(CommandOf(world.Active.Id, "notes.txt"), default);

        Assert.Equal("FilesArchiveFormatUnknown", result.Error!.Code);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
        Assert.Empty(world.Client.Calls);
        Assert.False(Assert.Single(world.Writer.Entries).Succeeded);
    }

    /// <summary>An archive the agent judges unsafe, or over its bounds, is the same refusal and is audited as a failure.</summary>
    /// <param name="agentCode">The agent's code for the refusal.</param>
    [Theory]
    [InlineData("AgentArchiveUnsafe")]
    [InlineData("AgentLimitReached")]
    public async Task An_archive_the_agent_refuses_is_a_refusal_and_is_audited_as_a_failure(string agentCode)
    {
        var world = FilesHandlerWorld.ForCustomer();
        world.Client.FailOn(
            nameof(IAgentFilesClient.ExtractArchiveAsync), string.Empty, Error.Of(agentCode, ErrorType.Validation));

        var result = await HandlerOf(world).HandleAsync(CommandOf(world.Active.Id), default);

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
            nameof(IAgentFilesClient.ExtractArchiveAsync),
            string.Empty,
            Error.Of("AgentQuotaExceeded", ErrorType.Conflict));

        var result = await HandlerOf(world).HandleAsync(CommandOf(world.Active.Id), default);

        Assert.Equal("FilesQuotaExceeded", result.Error!.Code);
    }

    /// <summary>Another tenant's account is not found and the agent is never asked.</summary>
    [Fact]
    public async Task Another_tenants_account_is_not_found_and_the_agent_is_never_asked()
    {
        var world = FilesHandlerWorld.ForCustomer();

        var result = await HandlerOf(world).HandleAsync(CommandOf(world.Stranger), default);

        Assert.Equal("AccountNotFound", result.Error!.Code);
        Assert.Empty(world.Client.Calls);
    }

    /// <summary>A suspended account refuses a customer and admits an administrator.</summary>
    [Fact]
    public async Task A_suspended_account_refuses_a_customer_and_admits_an_administrator()
    {
        var customer = FilesHandlerWorld.ForCustomer();
        var administrator = FilesHandlerWorld.ForAdministrator();

        var refused = await HandlerOf(customer).HandleAsync(CommandOf(customer.Suspended.Id), default);
        var admitted = await HandlerOf(administrator).HandleAsync(CommandOf(administrator.Suspended.Id), default);

        Assert.Equal("FilesAccountSuspended", refused.Error!.Code);
        Assert.Empty(customer.Client.Calls);
        Assert.True(admitted.IsSuccess);
    }
}
