using Maran.Modules.Backups.Common;
using Maran.Modules.Backups.Domain.Entities;
using Maran.Modules.Backups.Domain.Enums;

namespace Maran.Modules.Backups.Mappers;

/// <summary>Restates a <see cref="BackupDestination"/> as the wire shape a settings screen reads.</summary>
/// <remarks>
/// <para>
/// <b>It takes the local root as an argument because the row does not hold one.</b> The caller has
/// asked the agent what directory it writes into; this translates that answer, or its absence, onto
/// the view. A mapper translates and never decides, so it neither calls the agent nor invents a
/// value when the answer is missing — it carries the <c>null</c> through, and the screen says the
/// path is not established.
/// </para>
/// <para>
/// Separate from <see cref="BackupDestinationMapper"/>, which restates the same row for the AGENT.
/// The two translate one thing into two different contracts and share no field rule — the agent's
/// local destination carries no path at all, and the screen's exists to show one — so merging them
/// would put the reason a field is empty in one contract next to the reason it is filled in the
/// other. A mapper translates; neither of them decides.
/// </para>
/// </remarks>
public static class BackupDestinationViewMapper
{
    /// <summary>Builds the outward view of one destination.</summary>
    /// <param name="destination">The recorded destination.</param>
    /// <param name="agentBackupRoot">
    /// The directory the agent reported writing backups into, or <c>null</c> when the panel could
    /// not establish it. Used for a local destination and ignored for any other kind.
    /// </param>
    /// <param name="displayName">
    /// The name a screen shows for this destination, from <c>BackupDestinationDisplayNames</c>: the
    /// caller's language for the destination the panel seeded and named itself, and the stored name
    /// verbatim for one an operator named. Passed in for the reason the path is — a mapper
    /// translates and never decides, and a localized name needs the request's culture.
    /// </param>
    /// <returns>The wire shape: the row's own values, and a path only where one was established.</returns>
    public static BackupDestinationDto From(
        BackupDestination destination,
        string? agentBackupRoot,
        string displayName)
    {
        ArgumentNullException.ThrowIfNull(destination);

        var path = destination.Kind == BackupDestinationKind.Local
            ? NullIfEmpty(agentBackupRoot)
            : NullIfEmpty(destination.Path);

        return new BackupDestinationDto(
            destination.Id,
            destination.Name,
            displayName,
            destination.Kind,
            path,
            destination.IsDefault,
            destination.CreatedAt);
    }

    /// <summary>Renders an absent or empty path as the one value that means "not established".</summary>
    /// <param name="path">The path as it was established, or as the row holds it.</param>
    /// <returns>The path, or <c>null</c> when there is none.</returns>
    /// <remarks>
    /// An empty string reaches a screen as a rendered blank beside a label, which reads as "here is
    /// the path, and it is nothing"; <c>null</c> is the state the screen has a sentence for.
    /// </remarks>
    private static string? NullIfEmpty(string? path)
    {
        return string.IsNullOrEmpty(path) ? null : path;
    }
}
