using Maran.Modules.Backups.Domain.Enums;

namespace Maran.Modules.Backups.Domain.Entities;

/// <summary>
/// The panel's record of one backup of one account: when it was taken, why, how it ended, and the
/// digest a later restore verifies the artifact against (spec §11).
/// </summary>
/// <remarks>
/// <para>
/// <b><see cref="Id"/> is the agent's backup id.</b> One identifier crosses the whole system — this
/// row, the artifact's file name, the object key, and the id a restore or a delete names. The
/// alternative, a panel id plus an agent id with a mapping between them, is a place for the two to
/// disagree, and the symptom of that disagreement is a delete that removes the wrong customer's
/// archive.
/// </para>
/// <para>
/// <b>What is NOT here.</b> No path, no bucket, no object key, and no credential. Where the bytes
/// live is the destination's business and the agent's; a path in this table would be a second
/// statement of it, and the panel would have to be right about the agent's filesystem layout for
/// ever. It also means this row discloses nothing an operator's backup of the panel database should
/// not carry (rules/security.md item 8).
/// </para>
/// <para>
/// <b>A failure is recorded as a machine CODE, never as the agent's own sentence.</b> The agent's
/// text is logged at the client boundary and stops there (rules/csharp.md "Error carries a code and
/// nothing else"); a column that accepted it would put the agent's diagnostics — which can quote a
/// path, a database name, or a signed URL — into a table an administrator reads in a browser.
/// </para>
/// <para>
/// <b>Nothing has a public setter, and every transition is a method here.</b> A row is created
/// <see cref="BackupStatus.Running"/> and moves exactly once, to <see cref="Completed"/> or
/// <see cref="Failed"/>, from one terminal event. That is what makes "a completed row over a run
/// that failed" unrepresentable rather than merely unlikely (rules/csharp.md "Domain models are
/// rich").
/// </para>
/// </remarks>
public sealed class Backup
{
    /// <summary>The backup's identity, which is also the identifier the agent stores it under.</summary>
    public Guid Id { get; private set; }

    /// <summary>The account this is a backup of. Every tenant-scoped query is closed over this column.</summary>
    public Guid AccountId { get; private set; }

    /// <summary>The destination the artifact was written to, or <c>null</c> for the default one.</summary>
    /// <remarks>
    /// <para>
    /// <b>Every row written since destinations landed carries an identity; the nulls are history.</b>
    /// The column was nullable because no destination table existed, and <c>null</c> meant "the
    /// configured local root" rather than pointing at a row that was not there. That root is now a
    /// row — the default destination — so those nulls read correctly without being touched, and
    /// <c>BackupDestinationResolver</c> answers the same destination for a null as it does for that
    /// row's own id.
    /// </para>
    /// <para>
    /// <b>It stays nullable deliberately.</b> Narrowing it is an <c>AlterColumn</c> against a release
    /// that writes null on every insert, which the expand-then-contract law refuses and which no
    /// honest <c>contract-phase</c> marker could excuse; a backfill is impossible in a migration
    /// because the default row's identity is written at startup, not by the schema.
    /// </para>
    /// </remarks>
    public Guid? DestinationId { get; private set; }

    /// <summary>How far the run got, and therefore whether the artifact may be relied on.</summary>
    public BackupStatus Status { get; private set; }

    /// <summary>Why the backup was taken, which decides whether retention may remove it.</summary>
    public BackupKind Kind { get; private set; }

    /// <summary>The finished artifact's size in bytes, or zero while running or after a failure.</summary>
    public long SizeBytes { get; private set; }

    /// <summary>The finished artifact's SHA-256, hex and lowercase; empty until it completes.</summary>
    /// <remarks>
    /// The panel's copy of the digest, and the value a restore hands back to the agent as what it
    /// expects. Recording it here rather than reading it from the artifact's own sidecar at restore
    /// time is the point: a digest read from beside the bytes it describes proves only that the two
    /// were written together, which is exactly what an attacker who replaced both would arrange.
    /// </remarks>
    public string Sha256 { get; private set; }

    /// <summary>How many database dumps the finished archive contains.</summary>
    public int DatabaseCount { get; private set; }

    /// <summary>When the run began.</summary>
    public DateTimeOffset StartedAt { get; private set; }

    /// <summary>When the run ended, or <c>null</c> while it is still running.</summary>
    public DateTimeOffset? FinishedAt { get; private set; }

    /// <summary>The machine-stable code of the failure, or the empty string when nothing failed.</summary>
    public string FailureCode { get; private set; }

    /// <summary>
    /// The system user name of the account this backs up, recorded only once that account has been
    /// deleted; the empty string for every backup whose account still exists.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>It exists so that a row which outlives its account can still be released, and it was
    /// added because one could not be.</b> The artifact is addressed by the account's system user
    /// name (the agent stores it under <c>&lt;root&gt;/&lt;account&gt;/</c>), and every path that
    /// deletes one used to read that name from the accounts directory. For a
    /// <see cref="BackupKind.PreDeletion"/> row the directory answers "no such account" for ever, by
    /// design — so the deletion endpoint refused every one of them, permanently, and the rows and
    /// their archives accumulated with no code anywhere that could remove either.
    /// </para>
    /// <para>
    /// <b>It is stamped at deletion rather than carried from the start</b> because a name copied
    /// onto every row would be a second, staler statement of a fact the accounts table owns — and a
    /// customer who renames their account would leave every earlier row pointing at a directory that
    /// no longer exists. At the moment of deletion there is no longer an owner to disagree with,
    /// which is exactly when a copy becomes the only truth there is.
    /// </para>
    /// <para>
    /// A user name is not a path and not a secret: it is the name an administrator already sees on
    /// every screen in the panel, and it discloses nothing about where the bytes sit
    /// (rules/security.md item 8).
    /// </para>
    /// </remarks>
    public string OrphanedAccountUsername { get; private set; }

    /// <summary>Opens the record of a backup that has just been started.</summary>
    /// <param name="id">The identity, which is also the agent's backup id.</param>
    /// <param name="accountId">The account being backed up.</param>
    /// <param name="destinationId">The destination, or <c>null</c> for the configured local root.</param>
    /// <param name="kind">Why the backup is being taken.</param>
    /// <param name="startedAt">The instant the run began, taken from <see cref="IClock"/>.</param>
    /// <remarks>
    /// The row is written BEFORE the agent is asked to do anything, and it is written
    /// <see cref="BackupStatus.Running"/>. A row written afterwards would mean a run that the panel
    /// died in the middle of leaves an archive on disk that no row owns — invisible to the
    /// interface, invisible to retention, and counted against nothing.
    /// </remarks>
    public Backup(Guid id, Guid accountId, Guid? destinationId, BackupKind kind, DateTimeOffset startedAt)
    {
        Id = id;
        AccountId = accountId;
        DestinationId = destinationId;
        Kind = kind;
        Status = BackupStatus.Running;
        SizeBytes = 0;
        Sha256 = string.Empty;
        DatabaseCount = 0;
        StartedAt = startedAt;
        FinishedAt = null;
        FailureCode = string.Empty;
        OrphanedAccountUsername = string.Empty;
    }

    /// <summary>Parameterless constructor required by EF Core materialization.</summary>
    private Backup()
    {
        Sha256 = string.Empty;
        FailureCode = string.Empty;
        OrphanedAccountUsername = string.Empty;
    }

    /// <summary>Records that the agent produced a complete artifact.</summary>
    /// <param name="sizeBytes">The artifact's size.</param>
    /// <param name="sha256">The artifact's digest, hex and lowercase.</param>
    /// <param name="databaseCount">How many database dumps the archive contains.</param>
    /// <param name="finishedAt">The instant the run ended, taken from <see cref="IClock"/>.</param>
    /// <remarks>
    /// Refuses to complete a row that has already reached a terminal state, and refuses one with no
    /// digest. The second is the load-bearing half: a completed row with an empty
    /// <see cref="Sha256"/> is a backup the restore path would then have to hand the agent an empty
    /// expected digest for, and the agent refuses that — so the failure would surface at 3 a.m. on
    /// the one operation that must not be surprising, rather than here, where the artifact was
    /// described.
    /// </remarks>
    public void Completed(long sizeBytes, string sha256, int databaseCount, DateTimeOffset finishedAt)
    {
        if (Status != BackupStatus.Running || string.IsNullOrEmpty(sha256))
        {
            return;
        }

        Status = BackupStatus.Completed;
        SizeBytes = sizeBytes;
        Sha256 = sha256;
        DatabaseCount = databaseCount;
        FinishedAt = finishedAt;
        FailureCode = string.Empty;
    }

    /// <summary>Records that the run ended without a usable artifact.</summary>
    /// <param name="failureCode">The machine-stable code of the failure. Never a supplied sentence.</param>
    /// <param name="finishedAt">The instant the run ended, taken from <see cref="IClock"/>.</param>
    /// <remarks>
    /// Leaves <see cref="SizeBytes"/> and <see cref="Sha256"/> at their initial values rather than
    /// recording whatever partial figures the agent last reported: a size on a failed row reads as
    /// an artifact somebody could restore.
    /// </remarks>
    public void Failed(string failureCode, DateTimeOffset finishedAt)
    {
        if (Status != BackupStatus.Running)
        {
            return;
        }

        Status = BackupStatus.Failed;
        FinishedAt = finishedAt;
        FailureCode = failureCode;
    }

    /// <summary>Whether this backup's artifact may be deleted at the caller's request.</summary>
    /// <returns><c>true</c> when the run has ended, whichever way it ended.</returns>
    /// <remarks>
    /// A <see cref="BackupStatus.Running"/> backup is refused: its artifact is being written by the
    /// agent at this moment, and deleting the file underneath a running <c>tar</c> leaves a
    /// half-archive whose row then says <c>Completed</c>. A FAILED backup may be deleted — its row
    /// is the only thing left of it, and an operator clearing a screen full of failures is doing
    /// housekeeping, not destroying a copy.
    /// </remarks>
    public bool MayBeDeleted()
    {
        return Status != BackupStatus.Running;
    }

    /// <summary>Records that the account this backs up has been deleted, and under what name.</summary>
    /// <param name="accountUsername">The deleted account's system user name.</param>
    /// <remarks>
    /// <para>
    /// Called from this module's account cascade, on the one row the cascade keeps. It is what makes
    /// that row releasable afterwards: the archive is addressed by the account's user name, and after
    /// the cascade there is nowhere else left to read that name from.
    /// </para>
    /// <para>
    /// It refuses to overwrite a name it already holds, and refuses an empty one. Both are the same
    /// rule — the first stamp is the true one — and the consequence of getting it wrong is a delete
    /// aimed at a directory belonging to whoever took the name next.
    /// </para>
    /// </remarks>
    public void Orphan(string accountUsername)
    {
        if (string.IsNullOrEmpty(accountUsername) || OrphanedAccountUsername.Length > 0)
        {
            return;
        }

        OrphanedAccountUsername = accountUsername;
    }

    /// <summary>Whether this row's account has been deleted, leaving the row behind on purpose.</summary>
    /// <returns><c>true</c> once <see cref="Orphan"/> has recorded the deleted account's name.</returns>
    /// <remarks>
    /// The one question that decides where a caller reads the account's user name from: the accounts
    /// directory while the account exists, this row once it does not. It is a method here rather than
    /// an <c>OrphanedAccountUsername.Length > 0</c> at each call site so that "what makes a row an
    /// orphan" has one answer, and so that a future second marker cannot be added on one side only.
    /// </remarks>
    public bool NamesADeletedAccount()
    {
        return OrphanedAccountUsername.Length > 0;
    }

    /// <summary>Whether an unattended retention pass may remove this backup.</summary>
    /// <returns><c>true</c> only for a completed ordinary backup — manual or scheduled.</returns>
    /// <remarks>
    /// <para>
    /// <b>This is NOT <see cref="SurvivesAccountDeletion"/> asked the other way round, and the two
    /// must never be folded together.</b> That one answers "may the account cascade take this row",
    /// and its answer is about one kind. This one answers "may a nightly pass destroy the only copy
    /// of some bytes", and it is an ALLOW-list of kinds rather than a deny-list, on purpose: a kind
    /// added later is not prunable until somebody writes it down here, which is the safe direction
    /// for a mistake in a method that deletes customer data unattended.
    /// </para>
    /// <para>
    /// <b>What the allow-list keeps out, and why each one.</b>
    /// <see cref="BackupKind.PreDeletion"/> is the last copy of a deleted customer's data and a
    /// retention pass that ate it would undo the whole point of taking it;
    /// <see cref="BackupKind.PreRestore"/> is the copy taken immediately before an operation that
    /// replaces an account, and a safety copy retention can eat is not a safety copy (R12).
    /// </para>
    /// <para>
    /// <b>Only completed backups are prunable, so only completed backups are counted.</b> A failed
    /// row pins no artifact worth keeping and is not what an operator means by "keep seven" — if
    /// failures counted, a bad week would silently expire every good copy the account had.
    /// </para>
    /// </remarks>
    public bool MayBeRetentionPruned()
    {
        return Status == BackupStatus.Completed
            && (Kind == BackupKind.Manual || Kind == BackupKind.Scheduled);
    }

    /// <summary>Whether this row must be kept when the account it names is deleted.</summary>
    /// <returns><c>true</c> for the final backup taken before that very deletion.</returns>
    /// <remarks>
    /// <para>
    /// <b>The one row the account cascade must not remove is the one the cascade exists downstream
    /// of.</b> A <see cref="BackupKind.PreDeletion"/> backup is taken immediately before an account
    /// is destroyed, and it is the last copy of that customer's data in existence; a cascade that
    /// swept it away with the rest would delete the safety net in the same breath as the thing it
    /// was a net for.
    /// </para>
    /// <para>
    /// <b>The consequence is deliberate and is stated so it is never read as a bug:</b> such a row
    /// survives with an <see cref="AccountId"/> naming an account that no longer exists. It is
    /// therefore invisible to every customer — the tenant filter matches nobody — and visible to an
    /// administrator, which is exactly the pair that is wanted: the operator who deleted the account
    /// can find the archive, and no customer can be shown a stranger's.
    /// </para>
    /// <para>
    /// <b>It is a method here rather than a comparison at the call site</b> because two places have
    /// to agree about it — this module's account cascade and the Host's residue audit, which refuses
    /// a deletion over any row that still names the account. Two spellings of one rule is how the
    /// exemption widens by accident, and widening it would put back the hole the residue audit was
    /// written to close.
    /// </para>
    /// <para>
    /// Retention asks a DIFFERENT question and has its own predicate,
    /// <see cref="MayBeRetentionPruned"/>: that one prunes within a LIVING account and is an
    /// allow-list of kinds, so it keeps <see cref="BackupKind.PreRestore"/> out as well as
    /// <see cref="BackupKind.PreDeletion"/>. A predicate serving both would have to be right about
    /// two rules at once, and the day they diverge only one of them would be tested.
    /// </para>
    /// </remarks>
    public bool SurvivesAccountDeletion()
    {
        return Kind == BackupKind.PreDeletion;
    }
}
