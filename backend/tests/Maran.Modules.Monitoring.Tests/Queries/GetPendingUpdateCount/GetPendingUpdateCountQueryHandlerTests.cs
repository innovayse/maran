using Maran.Agent.Client.Services.MonitorService;
using Maran.Modules.Monitoring.Queries.GetPendingUpdateCount;
using Maran.Modules.Monitoring.Tests.TestSupport;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Monitoring.Tests.Queries.GetPendingUpdateCount;

/// <summary>
/// The navigation badge's figure (issue #324): the number of updates the agent reports, and a failed
/// read that stays a failure instead of becoming a zero.
/// </summary>
public sealed class GetPendingUpdateCountQueryHandlerTests
{
    /// <summary>The count is the number of updates the agent reported.</summary>
    [Fact]
    public async Task The_count_is_the_number_of_updates_the_agent_reported()
    {
        var agent = new StubAgentMonitorClient
        {
            PendingUpdates = Result<IReadOnlyList<AgentPendingUpdate>>.Ok(
            [
                new AgentPendingUpdate("bash", "2", "1", "updates", AgentUpdateClassification.Ordinary),
                new AgentPendingUpdate("curl", "2", null, string.Empty, AgentUpdateClassification.Ordinary),
                new AgentPendingUpdate("openssl", "2", "1", "security", AgentUpdateClassification.Security),
            ]),
        };
        var handler = new GetPendingUpdateCountQueryHandler(agent);

        var result = await handler.HandleAsync(new GetPendingUpdateCountQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value.Count);
    }

    /// <summary>A catalogue read successfully with nothing pending is a count of zero.</summary>
    [Fact]
    public async Task A_catalogue_read_successfully_with_nothing_pending_is_a_count_of_zero()
    {
        var handler = new GetPendingUpdateCountQueryHandler(new StubAgentMonitorClient());

        var result = await handler.HandleAsync(new GetPendingUpdateCountQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value.Count);
        Assert.Equal(0, result.Value.SecurityCount);
    }

    /// <summary>When every update was classified, the security count is the number marked as security fixes.</summary>
    [Fact]
    public async Task When_every_update_is_classified_the_security_count_is_the_number_marked_security()
    {
        var handler = Handler(
            Update("bash", AgentUpdateClassification.Ordinary),
            Update("curl", AgentUpdateClassification.Security),
            Update("openssl", AgentUpdateClassification.Security));

        var result = await handler.HandleAsync(new GetPendingUpdateCountQuery(), CancellationToken.None);

        Assert.Equal(3, result.Value.Count);
        Assert.Equal(2, result.Value.SecurityCount);
    }

    /// <summary>When every update was classified and none is a security fix, the security count is zero.</summary>
    [Fact]
    public async Task When_every_update_is_classified_and_none_is_security_the_security_count_is_zero()
    {
        var handler = Handler(
            Update("bash", AgentUpdateClassification.Ordinary),
            Update("curl", AgentUpdateClassification.Ordinary));

        var result = await handler.HandleAsync(new GetPendingUpdateCountQuery(), CancellationToken.None);

        Assert.Equal(2, result.Value.Count);
        Assert.Equal(0, result.Value.SecurityCount);
    }

    /// <summary>One unmeasured update makes the security count null, even beside a measured security fix.</summary>
    [Fact]
    public async Task One_unmeasured_update_makes_the_security_count_null()
    {
        var handler = Handler(
            Update("bash", AgentUpdateClassification.Security),
            Update("curl", AgentUpdateClassification.Unmeasured));

        var result = await handler.HandleAsync(new GetPendingUpdateCountQuery(), CancellationToken.None);

        Assert.Equal(2, result.Value.Count);
        Assert.Null(result.Value.SecurityCount);
    }

    /// <summary>A failed read is the agent's own failure and is never a count of zero.</summary>
    [Fact]
    public async Task A_failed_read_is_the_agents_own_failure_and_never_a_zero()
    {
        var agent = new StubAgentMonitorClient
        {
            PendingUpdates = Result<IReadOnlyList<AgentPendingUpdate>>.Fail(
                Error.Of("AgentSystemFailure", ErrorType.Failure)),
        };
        var handler = new GetPendingUpdateCountQueryHandler(agent);

        var result = await handler.HandleAsync(new GetPendingUpdateCountQuery(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentSystemFailure", result.Error!.Code);
        Assert.Equal(1, agent.PendingUpdatesCalls);
    }

    /// <summary>Builds the handler over an agent that reports exactly the given updates.</summary>
    /// <param name="updates">What the agent reports.</param>
    /// <returns>The handler.</returns>
    private static GetPendingUpdateCountQueryHandler Handler(params AgentPendingUpdate[] updates)
    {
        return new GetPendingUpdateCountQueryHandler(new StubAgentMonitorClient
        {
            PendingUpdates = Result<IReadOnlyList<AgentPendingUpdate>>.Ok(updates),
        });
    }

    /// <summary>Builds an update that names only its package and its classification.</summary>
    /// <param name="name">The package name.</param>
    /// <param name="classification">What the agent measured.</param>
    /// <returns>The update.</returns>
    private static AgentPendingUpdate Update(string name, AgentUpdateClassification classification)
    {
        return new AgentPendingUpdate(name, "2", "1", "updates", classification);
    }
}
