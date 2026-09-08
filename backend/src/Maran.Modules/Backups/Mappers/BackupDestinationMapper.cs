using Maran.Agent.Client.Services.BackupService;
using Maran.Modules.Backups.Domain.Entities;
using Maran.Modules.Backups.Domain.Enums;
using Maran.SharedKernel.Security;

namespace Maran.Modules.Backups.Mappers;

/// <summary>
/// Restates a recorded <see cref="BackupDestination"/> as the destination an agent call names.
/// </summary>
/// <remarks>
/// <para>
/// A mapper translates; it never decides (rules/csharp.md). Whether a destination of this kind may
/// be acted on at all is <c>RemoteDestinationPolicy</c>'s answer, asked by
/// <c>BackupDestinationResolver</c> before this is reached — so there is no branch here that could
/// refuse, and none that could silently substitute one kind for another.
/// </para>
/// <para>
/// <b><see cref="AgentBackupDestination.Path"/> is EMPTY for a local destination, and that is the
/// agent's rule rather than an omission.</b> The agent's <c>validated_destination</c> refuses a local
/// destination carrying a path — "a local destination carries no path: the agent's backup root is
/// its own" — because <c>ListBackups</c> carries no destination at all, so a per-call subdirectory
/// would produce artifacts no listing and therefore no retention could ever see. This mapper sent
/// the configured root in that field until 2026-09-08, which made every create, restore, delete and
/// retention call refusable by a real agent with <c>InvalidInput</c>; nothing caught it because every
/// panel-side test stubs the client. <c>Local_destinations_carry_no_path</c> is the test that now
/// does.
/// </para>
/// <para>
/// The S3 members are empty because no stored destination can carry them: this build has no path
/// that writes a remote destination, so there is no credential to restate. They are still wrapped in
/// <see cref="SensitiveString"/>, which is what makes a formatted destination print the mask — a
/// habit that has to exist before there is a secret, not after.
/// </para>
/// </remarks>
public static class BackupDestinationMapper
{
    /// <summary>Builds the agent-facing destination for one recorded row.</summary>
    /// <param name="destination">The destination the panel recorded.</param>
    /// <returns>The wire shape naming the same storage.</returns>
    public static AgentBackupDestination ForAgent(BackupDestination destination)
    {
        ArgumentNullException.ThrowIfNull(destination);

        var kind = destination.Kind == BackupDestinationKind.S3
            ? AgentBackupDestinationKind.S3
            : AgentBackupDestinationKind.Local;

        return new AgentBackupDestination(
            kind,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            new SensitiveString(string.Empty),
            new SensitiveString(string.Empty),
            S3PathStyle: false);
    }
}
