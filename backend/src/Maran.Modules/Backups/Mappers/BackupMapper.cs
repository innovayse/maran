using Maran.Modules.Backups.Common;
using Maran.Modules.Backups.Domain.Entities;

namespace Maran.Modules.Backups.Mappers;

/// <summary>Restates a <see cref="Backup"/> as the wire shape a screen reads.</summary>
/// <remarks>
/// A mapper translates; it never decides (rules/csharp.md). Every field is copied across
/// unchanged — there is no branch here, and there must not be one: whether a backup is usable is
/// <see cref="Backup.Status"/>, decided once when the terminal event arrived, and a mapper that
/// recomputed it would be a second, unreviewed statement of the same rule.
/// </remarks>
public static class BackupMapper
{
    /// <summary>Builds the outward view of one backup.</summary>
    /// <param name="backup">The recorded backup.</param>
    /// <param name="failureDisplayName">
    /// The row's failure code named in the caller's language, from <c>BackupFailureDisplayNames</c>,
    /// and empty when nothing failed. Passed in rather than looked up here because a mapper
    /// translates and never decides, and because resolving a localized name needs the request's
    /// culture, which a static translation has no business holding.
    /// </param>
    /// <returns>The wire shape carrying exactly what the row holds, and the name for its code.</returns>
    public static BackupDto From(Backup backup, string failureDisplayName)
    {
        ArgumentNullException.ThrowIfNull(backup);

        return new BackupDto(
            backup.Id,
            backup.AccountId,
            backup.OrphanedAccountUsername,
            backup.Status,
            backup.Kind,
            backup.SizeBytes,
            backup.Sha256,
            backup.DatabaseCount,
            backup.StartedAt,
            backup.FinishedAt,
            backup.FailureCode,
            failureDisplayName);
    }
}
