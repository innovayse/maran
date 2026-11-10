using Maran.Agent.Client.Services.TransferService;
using Maran.Modules.ServerMigration.Domain.Enums;
using Maran.Modules.ServerMigration.Domain.ValueObjects;

namespace Maran.Modules.ServerMigration.Mappers;

/// <summary>Restates what the agent established about a source as the snapshot the registry records.</summary>
public static class TestedSourceMapper
{
    /// <summary>Restates a test result.</summary>
    /// <param name="tested">What the agent's probes found.</param>
    /// <returns>The snapshot of that one test.</returns>
    public static TestedSourceSnapshot ToSnapshot(AgentTestedSource tested)
    {
        ArgumentNullException.ThrowIfNull(tested);

        return new TestedSourceSnapshot(
            tested.IsRoot,
            tested.HomeFreeBytes,
            tested.HomeTotalBytes,
            tested.SystemRelease,
            KindOf(tested.Kind),
            tested.MaranVersion,
            tested.DatabaseServer?.ToString(),
            tested.WebServer?.ToString(),
            tested.MailFound);
    }

    /// <summary>Restates the kind of system, when the agent could tell.</summary>
    /// <param name="kind">The agent's kind, or <c>null</c> when it could not tell.</param>
    /// <returns>This module's kind, or <c>null</c>.</returns>
    private static RemoteSourceKind? KindOf(SourceKind? kind)
    {
        return kind switch
        {
            null => null,
            SourceKind.GenericLinux => RemoteSourceKind.GenericLinux,
            SourceKind.Maran => RemoteSourceKind.Maran,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown source kind."),
        };
    }
}
