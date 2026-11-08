using Maran.Modules.Sites.Services;
using Maran.Modules.Sites.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Sites.Tests.Services;

/// <summary>Behavioral contract of <see cref="SiteLogPruner"/>.</summary>
public sealed class SiteLogPrunerTests
{
    /// <summary>The agent is asked to keep the site's own number of rotations.</summary>
    [Fact]
    public async Task The_agent_is_asked_to_keep_the_sites_own_number_of_rotations()
    {
        var site = SitesTestContext.PhpSite(Guid.NewGuid(), "example.com");
        site.ChangeLogRetention(3);
        var agent = new RecordingAgentSitesClient();

        var result = await Pruner(agent, new RecordingAuditWriter()).PruneAsync(site, "acme", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("prune:acme:example.com:3", Assert.Single(agent.Calls));
    }

    /// <summary>Removing files is journalled under the panel's own actor, with the domain as the subject.</summary>
    [Fact]
    public async Task Removing_files_is_journalled_under_the_panels_own_actor()
    {
        var site = SitesTestContext.PhpSite(Guid.NewGuid(), "example.com");
        var audit = new RecordingAuditWriter();

        await Pruner(new RecordingAgentSitesClient { PruneRemovedFiles = 4 }, audit)
            .PruneAsync(site, "acme", CancellationToken.None);

        var entry = Assert.Single(audit.Entries);
        Assert.Equal(SiteAuditActions.SiteLogsPruned, entry.Action);
        Assert.Equal("example.com", entry.Subject);
        Assert.Equal(SystemAuditEntry.NameFor(SiteAuditJournal.ModuleName), entry.ActorUsername);
        Assert.True(entry.Succeeded);
    }

    /// <summary>An hour in which nothing was removed writes no entry.</summary>
    [Fact]
    public async Task An_hour_in_which_nothing_was_removed_writes_no_entry()
    {
        var site = SitesTestContext.PhpSite(Guid.NewGuid(), "example.com");
        var audit = new RecordingAuditWriter();

        await Pruner(new RecordingAgentSitesClient { PruneRemovedFiles = 0 }, audit)
            .PruneAsync(site, "acme", CancellationToken.None);

        Assert.Empty(audit.Entries);
    }

    /// <summary>A refused prune is answered with the agent's code and writes no entry.</summary>
    [Fact]
    public async Task A_refused_prune_is_answered_with_the_agents_code_and_writes_no_entry()
    {
        var site = SitesTestContext.PhpSite(Guid.NewGuid(), "example.com");
        var audit = new RecordingAuditWriter();
        var agent = new RecordingAgentSitesClient { PruneFailure = Error.Of("AgentSystemFailure", ErrorType.Failure) };

        var result = await Pruner(agent, audit).PruneAsync(site, "acme", CancellationToken.None);

        Assert.Equal("AgentSystemFailure", result.Error!.Code);
        Assert.Empty(audit.Entries);
    }

    /// <summary>Builds the pruner under test.</summary>
    /// <param name="agent">The agent double.</param>
    /// <param name="audit">The journal double.</param>
    /// <returns>The pruner.</returns>
    private static SiteLogPruner Pruner(RecordingAgentSitesClient agent, RecordingAuditWriter audit)
    {
        return new SiteLogPruner(agent, new SiteAuditJournal(audit, FakeCurrentUser.Anonymous()));
    }
}
