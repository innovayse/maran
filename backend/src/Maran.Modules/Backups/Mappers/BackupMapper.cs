using Maran.Modules.Backups.Common;
using Maran.Modules.Backups.Domain.Entities;

namespace Maran.Modules.Backups.Mappers;

/// <summary>Restates a <see cref="Backup"/> as the wire shape a screen reads.</summary>
/// <remarks>
/// The one place a <see cref="BackupDto"/> is built, so the list, the single read and the create
/// answer cannot drift apart. A mapper translates; it never decides (rules/csharp.md). Every field
/// is copied across unchanged — there is no branch here, and there must not be one: whether a
/// backup is usable is <see cref="Backup.Status"/>, decided once when the terminal event arrived,
/// and a mapper that recomputed it would be a second, unreviewed statement of the same rule.
/// </remarks>
public static class BackupMapper
{
    /// <summary>Builds the outward view of one backup.</summary>
    /// <param name="backup">The recorded backup.</param>
    /// <param name="ownerNames">
    /// Account names the account directory answered for the rows being mapped, tenant-scoped. An
    /// account it did not name — deleted, or not the caller's to see — is absent, and the row's
    /// owner name is then empty; a deleted account's name travels in
    /// <see cref="BackupDto.OrphanedAccountUsername"/>, never here.
    /// </param>
    /// <param name="failureDisplayName">
    /// The row's failure code named in the caller's language, from <c>BackupFailureDisplayNames</c>,
    /// and empty when nothing failed. Passed in rather than looked up here because a mapper
    /// translates and never decides, and because resolving a localized name needs the request's
    /// culture, which a static translation has no business holding.
    /// </param>
    /// <returns>
    /// The wire shape carrying exactly what the row holds, its owner's name and the name for its code.
    /// </returns>
    public static BackupDto From(
        Backup backup,
        IReadOnlyDictionary<Guid, string> ownerNames,
        string failureDisplayName)
    {
        ArgumentNullException.ThrowIfNull(backup);
        ArgumentNullException.ThrowIfNull(ownerNames);

        return new BackupDto(
            backup.Id,
            backup.AccountId,
            ownerNames.GetValueOrDefault(backup.AccountId, string.Empty),
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
