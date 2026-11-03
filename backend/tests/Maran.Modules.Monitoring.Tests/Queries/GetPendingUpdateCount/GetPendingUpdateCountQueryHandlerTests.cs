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
                new AgentPendingUpdate("bash", "2", "1", "updates"),
                new AgentPendingUpdate("curl", "2", null, string.Empty),
                new AgentPendingUpdate("openssl", "2", "1", "security"),
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
}
