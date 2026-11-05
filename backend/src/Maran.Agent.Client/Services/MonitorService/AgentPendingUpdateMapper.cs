using Maran.Agent.V1;

namespace Maran.Agent.Client.Services.MonitorService;

/// <summary>Reads the wire's pending-update listing into the panel's own rows.</summary>
internal static class AgentPendingUpdateMapper
{
    /// <summary>Projects the wire update listing onto the panel's own rows.</summary>
    /// <param name="ok">The success payload of <c>ListPendingUpdates</c>.</param>
    /// <returns>One row per package, in the order the agent sent them.</returns>
    /// <remarks>
    /// The wire's empty <c>installed_version</c> means "this family does not report it" (dnf's
    /// <c>check-update</c> names only the new version), and protobuf cannot tell that from a version
    /// that is the empty string. The reading is made here, once, so the panel's row carries
    /// <c>null</c> for "not reported" and never an empty string a screen would print as a version.
    /// <c>source</c> may be empty on the wire and stays an empty string. The classification is read
    /// by <see cref="UpdateClassificationMapper"/>, so an unmeasured update stays unmeasured.
    /// </remarks>
    internal static List<AgentPendingUpdate> ToPendingUpdates(ListPendingUpdatesOk ok)
    {
        var updates = new List<AgentPendingUpdate>(ok.Updates.Count);

        foreach (var update in ok.Updates)
        {
            updates.Add(
                new AgentPendingUpdate(
                    update.PackageName,
                    update.AvailableVersion,
                    update.InstalledVersion.Length == 0 ? null : update.InstalledVersion,
                    update.Source,
                    UpdateClassificationMapper.ToUpdateClassification(update.Classification)));
        }

        return updates;
    }
}
