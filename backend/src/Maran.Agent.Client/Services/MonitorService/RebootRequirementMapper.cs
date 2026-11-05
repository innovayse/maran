using Maran.Agent.V1;

namespace Maran.Agent.Client.Services.MonitorService;

/// <summary>Reads the wire's reboot requirement into the panel's own (issue #163).</summary>
internal static class RebootRequirementMapper
{
    /// <summary>Projects the wire reboot requirement onto the panel's own.</summary>
    /// <param name="requirement">The requirement the agent reported.</param>
    /// <returns>The panel's value; <see cref="AgentRebootRequirement.Unmeasured"/> for anything unnamed.</returns>
    /// <remarks>
    /// Written out rather than cast. UNSPECIFIED, and a value from an agent newer than this panel, is
    /// a reply that states no requirement, so it is read ONCE here as unmeasured and never as
    /// "not required": the wire's zero must not become the answer an operator relies on.
    /// </remarks>
    internal static AgentRebootRequirement ToRebootRequirement(RebootRequirement requirement)
    {
        return requirement switch
        {
            RebootRequirement.NotRequired => AgentRebootRequirement.NotRequired,
            RebootRequirement.Required => AgentRebootRequirement.Required,
            _ => AgentRebootRequirement.Unmeasured,
        };
    }
}
