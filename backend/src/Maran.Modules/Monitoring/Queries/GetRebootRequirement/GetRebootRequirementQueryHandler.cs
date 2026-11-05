using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.MonitorService;
using Maran.Modules.Monitoring.Common;

namespace Maran.Modules.Monitoring.Queries.GetRebootRequirement;

/// <summary>
/// Handles <see cref="GetRebootRequirementQuery"/> by asking the agent to read the host's reboot marker
/// (issue #163).
/// </summary>
/// <remarks>
/// A probe that could not run is the agent's own typed failure and never "not required": a panel that
/// drew no notice because nothing was measured would tell the administrator the host is safe to leave.
/// A reading the agent did not make (an older agent) is <c>null</c>, which the interface shows as
/// nothing. Only a measured answer becomes <c>true</c> or <c>false</c>.
/// </remarks>
public sealed class GetRebootRequirementQueryHandler
{
    /// <summary>The agent, the only thing in the system that can read the host's reboot marker.</summary>
    private readonly IAgentMonitorClient _agent;

    /// <summary>Creates the handler.</summary>
    /// <param name="agent">The agent client that reads the reboot requirement.</param>
    public GetRebootRequirementQueryHandler(IAgentMonitorClient agent)
    {
        _agent = agent;
    }

    /// <summary>Returns whether a restart is required.</summary>
    /// <param name="query">The (parameterless) read request.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The measured requirement, or the agent's own typed failure.</returns>
    public async Task<Result<RebootRequirementDto>> HandleAsync(
        GetRebootRequirementQuery query,
        CancellationToken cancellationToken)
    {
        var requirement = await _agent.GetRebootRequirementAsync(cancellationToken);

        if (!requirement.IsSuccess)
        {
            return Result<RebootRequirementDto>.Fail(requirement.Error!);
        }

        bool? required = requirement.Value switch
        {
            AgentRebootRequirement.Required => true,
            AgentRebootRequirement.NotRequired => false,
            _ => null,
        };

        return Result<RebootRequirementDto>.Ok(new RebootRequirementDto(required));
    }
}
