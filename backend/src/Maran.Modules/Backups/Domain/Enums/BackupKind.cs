namespace Maran.Modules.Backups.Domain.Enums;

/// <summary>Why a backup was taken, which is what decides whether retention may remove it.</summary>
/// <remarks>
/// The kind is recorded at creation and never changes: it is the answer to "may this copy be
/// pruned", and a copy taken as a safety net before a destructive operation must not become an
/// ordinary daily that retention eats (the plan's R12).
///
/// All four values are declared now, and only <see cref="Manual"/> is produced today. This is the
/// one place a value ahead of its caller is right rather than speculative: the column is persisted
/// with the value's NAME, so adding a member later renumbers nothing but adding one to the middle
/// of a numeric mapping would — and, more importantly, retention and the deletion cascade are
/// written against the closed set, so a reader can see today which kinds they must never touch.
/// </remarks>
public enum BackupKind
{
    /// <summary>A customer or an administrator asked for it.</summary>
    Manual = 0,

    /// <summary>A schedule asked for it. Nothing produces this yet — schedules are later work.</summary>
    Scheduled = 1,

    /// <summary>
    /// Taken automatically before an account is deleted, and never pruned. Nothing produces this yet —
    /// the final backup before a deletion is later work.
    /// </summary>
    PreDeletion = 2,

    /// <summary>
    /// Taken automatically before a restore replaces the account, and never pruned. Nothing produces
    /// this yet — restore is later work.
    /// </summary>
    PreRestore = 3,
}
