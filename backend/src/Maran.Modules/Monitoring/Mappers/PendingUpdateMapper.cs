using Maran.Agent.Client.Services.MonitorService;
using Maran.Modules.Monitoring.Common;

namespace Maran.Modules.Monitoring.Mappers;

/// <summary>Translates what the agent reported for one pending update into the shape the API sends.</summary>
/// <remarks>
/// Restates and never decides: the client boundary has already turned an absent installed version
/// into <c>null</c> and an absent source into an empty string, once, so every field crosses as it arrived.
/// </remarks>
public static class PendingUpdateMapper
{
    /// <summary>Builds the wire row for one pending update.</summary>
    /// <param name="update">What the agent reported.</param>
    /// <returns>The row.</returns>
    public static PendingUpdateDto ToDto(AgentPendingUpdate update)
    {
        return new PendingUpdateDto(
            update.PackageName,
            update.AvailableVersion,
            update.InstalledVersion,
            update.Source);
    }
}
