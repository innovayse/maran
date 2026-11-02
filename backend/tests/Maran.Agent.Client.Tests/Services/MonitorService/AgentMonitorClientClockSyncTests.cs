using Maran.Agent.Client.Services.MonitorService;
using Maran.Agent.V1;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maran.Agent.Client.Tests.Services.MonitorService;

/// <summary>
/// Mapping contract of <see cref="AgentMonitorClient.GetClockSyncAsync"/>: each wire state reaches the
/// panel as its own value, and a reply that says nothing is refused rather than read as an answer.
/// </summary>
public sealed class AgentMonitorClientClockSyncTests
{
    /// <summary>Each wire state maps onto the panel's state of the same meaning.</summary>
    /// <param name="wire">The state the agent reports.</param>
    /// <param name="expected">The state the panel must read.</param>
    [Theory]
    [InlineData(ClockSyncState.Synchronized, AgentClockSyncState.Synchronized)]
    [InlineData(ClockSyncState.NotSynchronized, AgentClockSyncState.NotSynchronized)]
    [InlineData(ClockSyncState.SyncServiceDisabled, AgentClockSyncState.SyncServiceDisabled)]
    public async Task Each_wire_state_maps_onto_the_panel_state_of_the_same_meaning(
        ClockSyncState wire,
        AgentClockSyncState expected)
    {
        var stub = new StubMonitorService
        {
            ClockSyncResponse = new GetClockSyncResponse { Ok = new GetClockSyncOk { State = wire } },
        };

        var result = await Client(stub).GetClockSyncAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(expected, result.Value);
    }

    /// <summary>An unspecified state is refused, never read as synchronised or as not synchronised.</summary>
    [Fact]
    public async Task An_unspecified_state_is_refused_never_read_as_either_answer()
    {
        var stub = new StubMonitorService
        {
            ClockSyncResponse = new GetClockSyncResponse
            {
                Ok = new GetClockSyncOk { State = ClockSyncState.Unspecified },
            },
        };

        var result = await Client(stub).GetClockSyncAsync(CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentInvalidResponse", result.Error!.Code);
    }

    /// <summary>The agent's error payload maps to a failed result carrying the agent code.</summary>
    [Fact]
    public async Task The_error_payload_maps_to_a_failed_result_with_the_agent_code()
    {
        var stub = new StubMonitorService
        {
            ClockSyncResponse = new GetClockSyncResponse
            {
                Error = new AgentError { Code = ErrorCode.SystemFailure, Message = "timedated unreachable" },
            },
        };

        var result = await Client(stub).GetClockSyncAsync(CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentSystemFailure", result.Error!.Code);
    }

    /// <summary>A response with neither branch set is refused.</summary>
    [Fact]
    public async Task A_response_with_neither_branch_set_is_refused()
    {
        var result = await Client(new StubMonitorService()).GetClockSyncAsync(CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentInvalidResponse", result.Error!.Code);
    }

    /// <summary>Builds the client under test over the given stub.</summary>
    /// <param name="stub">The stubbed transport.</param>
    /// <returns>The client.</returns>
    private static AgentMonitorClient Client(StubMonitorService stub)
    {
        return new AgentMonitorClient(stub, NullLogger<AgentMonitorClient>.Instance);
    }
}
