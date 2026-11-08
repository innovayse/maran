using Maran.Agent.Client.Interfaces;
using Maran.Modules.Files.Jobs;
using Maran.Modules.Files.Options;
using Maran.Modules.Files.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maran.Modules.Files.Tests.Jobs;

/// <summary>
/// Holds <see cref="TrashRetentionHandler"/> to purging expired trash for every account including a suspended
/// one, carrying on past an account the agent refuses, journalling only what removed something or failed, and
/// taking the age from the configured retention.
/// </summary>
public sealed class TrashRetentionHandlerTests
{
    /// <summary>Builds the handler over a world's collaborators.</summary>
    /// <param name="world">The world.</param>
    /// <returns>The handler.</returns>
    private static TrashRetentionHandler HandlerOf(FilesHandlerWorld world)
    {
        return new TrashRetentionHandler(
            world.Directory, world.Client, world.Journal, world.Monitor, NullLogger<TrashRetentionHandler>.Instance);
    }

    /// <summary>Every account is purged, a suspended one included, and each removal is journalled by the system actor.</summary>
    [Fact]
    public async Task Every_account_is_purged_including_a_suspended_one_and_each_removal_is_journalled()
    {
        var world = FilesHandlerWorld.ForCustomer();
        world.Client.PurgedCount = 2;

        var removed = await HandlerOf(world).HandleAsync(new TrashRetentionRequested(), default);

        Assert.Equal(4, removed);
        Assert.Equal(["alpha", "beta"], world.Client.PurgeRequests.Select(request =>
        {
            return request.Account;
        }).Order());
        Assert.Equal(2, world.Writer.Entries.Count);
        Assert.All(world.Writer.Entries, entry =>
        {
            Assert.Equal(FileAuditActions.FileTrashRetentionPurged, entry.Action);
            Assert.Equal("maran-files", entry.ActorUsername);
            Assert.True(entry.Succeeded);
        });
        Assert.Equal(["alpha:", "beta:"], world.Writer.Entries.Select(entry =>
        {
            return entry.Subject;
        }).Order());
    }

    /// <summary>One account the agent refuses does not stop the others, and its failure is journalled.</summary>
    [Fact]
    public async Task One_account_the_agent_refuses_does_not_stop_the_others()
    {
        var world = FilesHandlerWorld.ForCustomer();
        world.Client.FailOn(
            nameof(IAgentFilesClient.PurgeExpiredTrashAsync), "alpha", Error.Of("AgentSaturated", ErrorType.Unavailable));

        var removed = await HandlerOf(world).HandleAsync(new TrashRetentionRequested(), default);

        Assert.Equal(2, removed);
        Assert.Equal(2, world.Client.PurgeRequests.Count);
        var failed = Assert.Single(world.Writer.Entries, entry =>
        {
            return !entry.Succeeded;
        });
        Assert.Equal("alpha:", failed.Subject);
        var succeeded = Assert.Single(world.Writer.Entries, entry =>
        {
            return entry.Succeeded;
        });
        Assert.Equal("beta:", succeeded.Subject);
    }

    /// <summary>A pass that removed nothing writes no entry, so the lines that matter are not buried.</summary>
    [Fact]
    public async Task A_pass_that_removed_nothing_writes_no_entry()
    {
        var world = FilesHandlerWorld.ForCustomer();
        world.Client.PurgedCount = 0;

        var removed = await HandlerOf(world).HandleAsync(new TrashRetentionRequested(), default);

        Assert.Equal(0, removed);
        Assert.Empty(world.Writer.Entries);
        Assert.Equal(2, world.Client.PurgeRequests.Count);
    }

    /// <summary>The age past which entries go is the configured retention in days.</summary>
    [Fact]
    public async Task The_age_past_which_entries_go_is_the_configured_retention()
    {
        var world = FilesHandlerWorld.ForCustomer(new FilesOptions { TrashRetentionDays = 7 });

        await HandlerOf(world).HandleAsync(new TrashRetentionRequested(), default);

        Assert.All(world.Client.PurgeRequests, request =>
        {
            Assert.Equal(7u, request.Days);
        });
    }
}
