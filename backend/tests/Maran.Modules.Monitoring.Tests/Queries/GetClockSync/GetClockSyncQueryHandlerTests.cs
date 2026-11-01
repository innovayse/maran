using System.Globalization;
using Maran.Agent.Client.Services.MonitorService;
using Maran.Modules.Monitoring.Queries.GetClockSync;
using Maran.Modules.Monitoring.Tests.TestSupport;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Monitoring.Tests.Queries.GetClockSync;

/// <summary>
/// The clock card's read (issue #150): the agent's state crosses as itself with an explanation from
/// the real resource files, and a failed read stays a failure.
/// </summary>
public sealed class GetClockSyncQueryHandlerTests
{
    /// <summary>Every state has its own explanation in every shipped language.</summary>
    /// <param name="state">The state the agent reports.</param>
    /// <param name="culture">The request culture.</param>
    [Theory]
    [InlineData(AgentClockSyncState.Synchronized, "en")]
    [InlineData(AgentClockSyncState.NotSynchronized, "en")]
    [InlineData(AgentClockSyncState.SyncServiceDisabled, "en")]
    [InlineData(AgentClockSyncState.Synchronized, "ru")]
    [InlineData(AgentClockSyncState.NotSynchronized, "ru")]
    [InlineData(AgentClockSyncState.SyncServiceDisabled, "ru")]
    [InlineData(AgentClockSyncState.Synchronized, "hy")]
    [InlineData(AgentClockSyncState.NotSynchronized, "hy")]
    [InlineData(AgentClockSyncState.SyncServiceDisabled, "hy")]
    public async Task Every_state_has_its_own_explanation_in_every_language(AgentClockSyncState state, string culture)
    {
        var agent = new StubAgentMonitorClient { ClockSync = Result<AgentClockSyncState>.Ok(state) };
        var handler = new GetClockSyncQueryHandler(agent, MonitoringTestContext.DisplayNamesText());
        var previous = CultureInfo.CurrentUICulture;

        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo(culture);

            var result = await handler.HandleAsync(new GetClockSyncQuery(), CancellationToken.None);

            Assert.True(result.IsSuccess);
            Assert.Equal(state, result.Value.State);
            Assert.NotEqual("ClockSync" + state, result.Value.Description);
            Assert.False(string.IsNullOrWhiteSpace(result.Value.Description));
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    /// <summary>A failed read is the agent's own failure, never a state.</summary>
    [Fact]
    public async Task A_failed_read_is_the_agents_own_failure()
    {
        var agent = new StubAgentMonitorClient
        {
            ClockSync = Result<AgentClockSyncState>.Fail(Error.Of("AgentSystemFailure", ErrorType.Failure)),
        };
        var handler = new GetClockSyncQueryHandler(agent, MonitoringTestContext.DisplayNamesText());

        var result = await handler.HandleAsync(new GetClockSyncQuery(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentSystemFailure", result.Error!.Code);
    }
}
