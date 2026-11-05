using Grpc.Core;
using Maran.Agent.Client.Services.MonitorService;
using Maran.Agent.V1;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maran.Agent.Client.Tests.Services.MonitorService;

/// <summary>
/// Mapping contract of <see cref="AgentMonitorClient.GetRebootRequirementAsync"/> (issue #163): each measured
/// answer reaches the panel as itself, an unnamed value is unmeasured and never "not required", and a probe
/// that could not run is a failure, never an answer.
/// </summary>
public sealed class AgentMonitorClientRebootRequirementTests
{
    /// <summary>Each measured wire answer maps onto the panel value of the same meaning.</summary>
    /// <param name="wire">The requirement the agent reports.</param>
    /// <param name="expected">The value the panel must read.</param>
    [Theory]
    [InlineData(RebootRequirement.Required, AgentRebootRequirement.Required)]
    [InlineData(RebootRequirement.NotRequired, AgentRebootRequirement.NotRequired)]
    public async Task Each_measured_wire_answer_maps_onto_the_panel_value_of_the_same_meaning(
        RebootRequirement wire,
        AgentRebootRequirement expected)
    {
        var stub = ReportingRequirement(wire);

        var result = await Client(stub).GetRebootRequirementAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(expected, result.Value);
        Assert.NotNull(stub.LastRebootRequirementRequest);
    }

    /// <summary>An unspecified answer is unmeasured, never read as "not required".</summary>
    [Fact]
    public async Task An_unspecified_answer_is_unmeasured_never_not_required()
    {
        var result = await Client(ReportingRequirement(RebootRequirement.Unspecified))
            .GetRebootRequirementAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(AgentRebootRequirement.Unmeasured, result.Value);
    }

    /// <summary>A value a newer agent added is unmeasured, never a guessed answer.</summary>
    [Fact]
    public async Task An_answer_this_build_cannot_name_is_unmeasured()
    {
        var result = await Client(ReportingRequirement((RebootRequirement)99))
            .GetRebootRequirementAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(AgentRebootRequirement.Unmeasured, result.Value);
    }

    /// <summary>A probe the agent could not run is a failure carrying the agent code, never "not required".</summary>
    [Fact]
    public async Task A_probe_that_could_not_run_is_a_failure_carrying_the_agent_code()
    {
        var stub = new StubMonitorService
        {
            RebootRequirementResponse = new GetRebootRequirementResponse
            {
                Error = new AgentError { Code = ErrorCode.SystemFailure, Message = "needs-restarting is missing" },
            },
        };

        var result = await Client(stub).GetRebootRequirementAsync(CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentSystemFailure", result.Error!.Code);
    }

    /// <summary>A response with neither branch set is refused rather than read as an answer.</summary>
    [Fact]
    public async Task A_response_with_neither_branch_set_is_refused()
    {
        var result = await Client(new StubMonitorService()).GetRebootRequirementAsync(CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentInvalidResponse", result.Error!.Code);
    }

    /// <summary>
    /// A transport failure is not mapped by this client: it propagates to the resilience decorator, and is never
    /// answered as a requirement.
    /// </summary>
    [Fact]
    public async Task A_transport_failure_propagates_and_is_never_answered_as_a_requirement()
    {
        var failure = new RpcException(new Status(StatusCode.Unavailable, "transport"));
        var stub = new StubMonitorService { RebootRequirementFailure = failure };

        var thrown = await Assert.ThrowsAsync<RpcException>(
            () =>
            {
                return Client(stub).GetRebootRequirementAsync(CancellationToken.None);
            });

        Assert.Same(failure, thrown);
    }

    /// <summary>Builds a stub answering the reboot probe with one requirement.</summary>
    /// <param name="requirement">The requirement the agent reports.</param>
    /// <returns>The configured stub.</returns>
    private static StubMonitorService ReportingRequirement(RebootRequirement requirement)
    {
        return new StubMonitorService
        {
            RebootRequirementResponse = new GetRebootRequirementResponse
            {
                Ok = new GetRebootRequirementOk { Requirement = requirement },
            },
        };
    }

    /// <summary>Builds the client under test over the given stub.</summary>
    /// <param name="stub">The stubbed transport.</param>
    /// <returns>The client.</returns>
    private static AgentMonitorClient Client(StubMonitorService stub)
    {
        return new AgentMonitorClient(stub, NullLogger<AgentMonitorClient>.Instance);
    }
}
