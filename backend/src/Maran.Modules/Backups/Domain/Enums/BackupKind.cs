namespace Maran.Modules.Backups.Domain.Enums;

/// <summary>Why a backup was taken, which is what decides whether retention may remove it.</summary>
/// <remarks>
/// The kind is recorded at creation and never changes: it is the answer to "may this copy be
/// pruned", and a copy taken as a safety net before a destructive operation must not become an
/// ordinary daily that retention eats (the plan's R12).
///
/// All five values are produced: <see cref="Manual"/> and <see cref="Scheduled"/> by the create path,
/// <see cref="PreDeletion"/>, <see cref="PreRestore"/> and <see cref="Migration"/> by <c>SafetyBackupService</c>
/// and <c>MigrationArtifactService</c>. Retention
/// and the deletion cascade are written against this closed set, so a reader can see which kinds
/// they must never touch.
/// </remarks>
public enum BackupKind
{
    /// <summary>A customer or an administrator asked for it.</summary>
    Manual = 0,

    /// <summary>A schedule asked for it; <c>BackupRunHandler</c> is its only writer.</summary>
    Scheduled = 1,

    /// <summary>
    /// Taken automatically before an account is deleted, and never pruned; <c>AccountBackupService</c>
    /// writes it.
    /// </summary>
    PreDeletion = 2,

    /// <summary>
    /// Taken automatically before a restore replaces the account, and never pruned;
    /// <c>RestoreBackupCommandHandler</c> asks <c>SafetyBackupService</c> for it and refuses the
    /// restore when it cannot be made. The agent's restore does not take it (it expects its caller
    /// to), so this kind has exactly one meaning: the copy of the account as it was the moment before
    /// a restore replaced it.
    /// </summary>
    PreRestore = 3,

    /// <summary>
    /// Made to be exported to another Maran server, or published here by an import (issue #127). It is in
    /// none of retention's allow-lists: it is not pruned by a schedule's retain figure, does not count against
    /// a customer's backup allowance, and is not a customer's to delete. Unlike <see cref="PreDeletion"/> it
    /// does not outlive its account: it is a copy of data that moves, not the last copy of a deleted
    /// customer's data.
    /// </summary>
    Migration = 4,
}
