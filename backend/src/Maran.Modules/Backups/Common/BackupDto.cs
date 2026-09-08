using Maran.Modules.Backups.Domain.Enums;

namespace Maran.Modules.Backups.Common;

/// <summary>Outward view of one backup: everything a screen shows about it.</summary>
/// <remarks>
/// One DTO for the list and for the single read, because a backup has no detail beyond this — there
/// is no larger view to grow into, and a second near-identical type would only be somewhere for the
/// two to drift apart.
///
/// It carries no path, no bucket and no object key, because the entity holds none
/// (<see cref="Domain.Entities.Backup"/>): where the bytes live is not a customer's business, and an
/// administrator who needs to know reads the destination, not one backup's row.
///
/// It DOES carry the digest, which is deliberate rather than an oversight. A SHA-256 of an archive
/// is not a secret — it discloses nothing about the contents and cannot be inverted — and it is the
/// one value an operator restoring from a rescue shell needs in order to check that the file they
/// found is the file the panel recorded.
/// </remarks>
/// <param name="Id">The backup's identity, which is also the identifier its artifact is stored under.</param>
/// <param name="AccountId">The account this is a backup of.</param>
/// <param name="Status">How far the run got, and therefore whether a restore may read it.</param>
/// <param name="Kind">Why the backup was taken.</param>
/// <param name="SizeBytes">The artifact's size, or zero when there is no artifact.</param>
/// <param name="Sha256">The artifact's digest, hex and lowercase, or empty when there is no artifact.</param>
/// <param name="DatabaseCount">How many database dumps the archive contains.</param>
/// <param name="StartedAt">When the run began.</param>
/// <param name="FinishedAt">When the run ended, or <c>null</c> while it is still running.</param>
/// <param name="FailureCode">The machine-stable code of the failure, or empty when nothing failed.</param>
/// <param name="FailureDisplayName">
/// The same failure named in the caller's language, or empty when nothing failed. It travels BESIDE
/// <paramref name="FailureCode"/> rather than replacing it: the code is what a script, a support
/// ticket and a log line are matched on and must not change with a request's culture, and the name
/// is what a table cell shows. A screen that had only the code printed it — an operator read
/// "Не удалась AgentSystemFailure" — and a wire that carried only the name would have taken the
/// machine-stable value away from every reader that needs it.
/// </param>
public sealed record BackupDto(
    Guid Id,
    Guid AccountId,
    BackupStatus Status,
    BackupKind Kind,
    long SizeBytes,
    string Sha256,
    int DatabaseCount,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    string FailureCode,
    string FailureDisplayName);
