using Maran.Agent.Client.Services.MonitorService;
using Maran.Modules.Monitoring.Common;
using Maran.Modules.Monitoring.Queries.GetPendingUpdateCount;
using Maran.Modules.Monitoring.Queries.GetRebootRequirement;
using Maran.Modules.Monitoring.Queries.GetServerStatus;
using Maran.Modules.Monitoring.Tests.TestSupport;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Monitoring.Tests.Queries.GetServerStatus;

/// <summary>
/// The server status an API key reads (issue #144) is exactly the existing two reads composed:
/// measured figures pass through, an unmeasured one stays null, and a failed read is the whole answer.
/// </summary>
/// <remarks>
/// The bus is answered by the REAL pending-update and reboot handlers over the stub agent, so the
/// composition is held against what those handlers produce and not against a hand-written copy of it.
/// </remarks>
public sealed class GetServerStatusQueryHandlerTests
{
    /// <summary>Measured figures reach the status unchanged.</summary>
    [Fact]
    public async Task Measured_figures_reach_the_status_unchanged()
    {
        var agent = new StubAgentMonitorClient
        {
            PendingUpdates = Result<IReadOnlyList<AgentPendingUpdate>>.Ok(
            [
                new AgentPendingUpdate("bash", "2", "1", "updates", AgentUpdateClassification.Ordinary),
                new AgentPendingUpdate("openssl", "2", "1", "security", AgentUpdateClassification.Security),
            ]),
            RebootRequirement = Result<AgentRebootRequirement>.Ok(AgentRebootRequirement.Required),
        };

        var result = await Handler(agent).HandleAsync(new GetServerStatusQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(new ServerStatusDto(2, 1, true), result.Value);
    }

    /// <summary>A reboot reading the agent did not make stays null and is never reported as false.</summary>
    [Fact]
    public async Task An_unmeasured_reboot_requirement_stays_null()
    {
        var agent = new StubAgentMonitorClient
        {
            RebootRequirement = Result<AgentRebootRequirement>.Ok(AgentRebootRequirement.Unmeasured),
        };

        var result = await Handler(agent).HandleAsync(new GetServerStatusQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.RebootRequired);
        Assert.Equal(0, result.Value.PendingUpdateCount);
    }

    /// <summary>One unclassified update leaves the security count null and never zero.</summary>
    [Fact]
    public async Task An_unclassified_update_leaves_the_security_count_null()
    {
        var agent = new StubAgentMonitorClient
        {
            PendingUpdates = Result<IReadOnlyList<AgentPendingUpdate>>.Ok(
            [
                new AgentPendingUpdate("bash", "2", "1", "updates", AgentUpdateClassification.Unmeasured),
            ]),
        };

        var result = await Handler(agent).HandleAsync(new GetServerStatusQuery(), CancellationToken.None);

        Assert.Equal(1, result.Value.PendingUpdateCount);
        Assert.Null(result.Value.SecurityUpdateCount);
    }

    /// <summary>A failed update read is the whole answer, and the reboot reading is not asked for.</summary>
    [Fact]
    public async Task A_failed_update_read_is_the_typed_error_and_nothing_more_is_asked()
    {
        var failure = Error.Of("AgentSystemFailure", ErrorType.Failure);
        var agent = new StubAgentMonitorClient { PendingUpdates = Result<IReadOnlyList<AgentPendingUpdate>>.Fail(failure) };

        var result = await Handler(agent).HandleAsync(new GetServerStatusQuery(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(failure, result.Error);
        Assert.Equal(0, agent.RebootRequirementCalls);
    }

    /// <summary>A failed reboot read is the whole answer, never a status with a missing half.</summary>
    [Fact]
    public async Task A_failed_reboot_read_is_the_typed_error()
    {
        var failure = Error.Of("AgentSystemFailure", ErrorType.Failure);
        var agent = new StubAgentMonitorClient { RebootRequirement = Result<AgentRebootRequirement>.Fail(failure) };

        var result = await Handler(agent).HandleAsync(new GetServerStatusQuery(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(failure, result.Error);
    }

    /// <summary>Builds the handler over a bus that answers with the real existing query handlers.</summary>
    /// <param name="agent">The stub agent both existing handlers read.</param>
    /// <returns>The handler under test.</returns>
    private static GetServerStatusQueryHandler Handler(StubAgentMonitorClient agent)
    {
        var bus = new RespondingMessageBus
        {
            Responder = async message =>
            {
                return message switch
                {
                    GetPendingUpdateCountQuery query => await new GetPendingUpdateCountQueryHandler(agent)
                        .HandleAsync(query, CancellationToken.None),
                    GetRebootRequirementQuery query => await new GetRebootRequirementQueryHandler(agent)
                        .HandleAsync(query, CancellationToken.None),
                    _ => throw new NotSupportedException(),
                };
            },
        };

        return new GetServerStatusQueryHandler(bus);
    }
}
