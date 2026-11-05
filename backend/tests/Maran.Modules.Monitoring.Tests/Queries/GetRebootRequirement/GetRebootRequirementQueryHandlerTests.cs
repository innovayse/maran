using Maran.Agent.Client.Services.MonitorService;
using Maran.Modules.Monitoring.Common;
using Maran.Modules.Monitoring.Queries.GetRebootRequirement;
using Maran.Modules.Monitoring.Tests.TestSupport;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Monitoring.Tests.Queries.GetRebootRequirement;

/// <summary>
/// The reboot-required notice's figure (issue #163): only a measured answer becomes true or false, an
/// answer nobody measured stays null, and a probe that could not run stays a failure.
/// </summary>
public sealed class GetRebootRequirementQueryHandlerTests
{
    /// <summary>A host whose marker says a restart is needed answers required.</summary>
    [Fact]
    public async Task A_host_that_needs_a_restart_answers_required()
    {
        var result = await HandleAsync(AgentRebootRequirement.Required);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.Required);
    }

    /// <summary>A host whose marker says none is needed answers not required: the inverse control.</summary>
    [Fact]
    public async Task A_host_that_needs_no_restart_answers_not_required()
    {
        var result = await HandleAsync(AgentRebootRequirement.NotRequired);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.Required);
    }

    /// <summary>An answer the agent did not measure is null and is never read as not required.</summary>
    [Fact]
    public async Task An_unmeasured_answer_is_null_and_never_not_required()
    {
        var result = await HandleAsync(AgentRebootRequirement.Unmeasured);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.Required);
    }

    /// <summary>A probe that could not run is the agent's own failure and is never not required.</summary>
    [Fact]
    public async Task A_probe_that_could_not_run_is_the_agents_own_failure()
    {
        var agent = new StubAgentMonitorClient
        {
            RebootRequirement = Result<AgentRebootRequirement>.Fail(
                Error.Of("AgentSystemFailure", ErrorType.Failure)),
        };
        var handler = new GetRebootRequirementQueryHandler(agent);

        var result = await handler.HandleAsync(new GetRebootRequirementQuery(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentSystemFailure", result.Error!.Code);
        Assert.Equal(ErrorType.Failure, result.Error.Type);
        Assert.Equal(1, agent.RebootRequirementCalls);
    }

    /// <summary>Runs the handler over an agent that answers exactly the given requirement.</summary>
    /// <param name="requirement">What the agent reports.</param>
    /// <returns>The handler's result.</returns>
    private static async Task<Result<RebootRequirementDto>> HandleAsync(
        AgentRebootRequirement requirement)
    {
        var agent = new StubAgentMonitorClient
        {
            RebootRequirement = Result<AgentRebootRequirement>.Ok(requirement),
        };
        var handler = new GetRebootRequirementQueryHandler(agent);

        return await handler.HandleAsync(new GetRebootRequirementQuery(), CancellationToken.None);
    }
}
