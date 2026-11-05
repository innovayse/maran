using Maran.Agent.V1;

namespace Maran.Agent.Client.Services.MonitorService;

/// <summary>Reads the wire's update classification into the panel's own (issue #163).</summary>
internal static class UpdateClassificationMapper
{
    /// <summary>Projects the wire update classification onto the panel's own.</summary>
    /// <param name="classification">The classification the agent reported for one update.</param>
    /// <returns>The panel's value; <see cref="AgentUpdateClassification.Unmeasured"/> for anything unnamed.</returns>
    /// <remarks>
    /// Written out rather than cast. An agent older than revision 19 never sets the field, which
    /// protobuf reads as UNSPECIFIED, and that is "not measured", never "ordinary": the reading is made
    /// once here so the panel's row never carries the wire's zero as a verdict.
    /// </remarks>
    internal static AgentUpdateClassification ToUpdateClassification(UpdateClassification classification)
    {
        return classification switch
        {
            UpdateClassification.Security => AgentUpdateClassification.Security,
            UpdateClassification.Ordinary => AgentUpdateClassification.Ordinary,
            _ => AgentUpdateClassification.Unmeasured,
        };
    }
}
