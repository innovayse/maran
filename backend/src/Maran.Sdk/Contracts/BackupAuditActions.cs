namespace Maran.Sdk.Contracts;

/// <summary>
/// The machine-stable audit action names for backup, restore, schedule, retention and destination actions. Constants rather than an enum: a marketplace
/// module records actions this assembly was never compiled knowing about, and an enum could not be
/// extended from outside.
/// </summary>
/// <remarks>
/// One of the Sdk's <c>*AuditActions</c> classes, which together are the closed set of actions the
/// shipped modules record; each is named for the area whose journal writes its constants.
/// </remarks>
public static class BackupAuditActions
{
    /// <summary>
    /// An account was replaced from one of its backups. Recorded on the refusals AND on the partial
    /// restores, and the partial ones are why this action exists at all: a restore that replaced the
    /// home and lost a database has changed the account and did not do what was asked, and the entry
    /// an operator finds must say so rather than reading as a clean restore.
    /// </summary>
    public const string BackupRestored = "BackupRestored";

    /// <summary>
    /// A backup of an account was taken. Recorded on the failures too, and that half is the half
    /// that matters: a backup nobody noticed had stopped working is discovered on the day it is
    /// needed. The subject is the account's name — what the backup acts on, as an operator would
    /// search for it — and never a path: an entry naming where an archive of a customer's database
    /// sits would be a permanent, searchable index of exactly that (rules/security.md item 8).
    /// </summary>
    public const string BackupCreated = "BackupCreated";

    /// <summary>
    /// A backup's archive and its record were deleted. Recorded on the refusals too, because a
    /// deletion refused for a backup the caller may not see is how one caller learns another
    /// account holds it — and because the destruction of the last copy of a customer's data needs a
    /// date and an author whether or not it succeeded.
    /// </summary>
    public const string BackupDeleted = "BackupDeleted";

    /// <summary>
    /// A backup schedule was created or replaced: how often the panel backs an account up, and how
    /// many copies it keeps. Recorded on the refusals too. Both halves of that setting destroy data
    /// eventually — lowering the retained count is what makes the next unattended pass delete
    /// archives that were being kept yesterday — so the change needs a date and an author as much as
    /// any single deletion does.
    /// </summary>
    public const string BackupScheduleSaved = "BackupScheduleSaved";

    /// <summary>
    /// A backup's archive and its record were removed by the unattended retention pass, because the
    /// account already had as many copies as its schedule keeps. One entry per backup, and the actor
    /// is the panel itself: nobody asked for this that night, which is exactly why the line has to
    /// exist. Recorded on the refusals too — a retention pass the agent refused is an account whose
    /// archives are silently no longer bounded.
    /// </summary>
    public const string BackupRetentionPruned = "BackupRetentionPruned";

    /// <summary>
    /// An administrator asked this server to record a place to keep backups. Every one of those
    /// requests is refused on a build that stores backups locally only — a second local root the
    /// agent could not be told about, or the S3-compatible storage this build has no code for — and
    /// the refusal is exactly what makes the line worth keeping: it is the record of the remote
    /// storage an operator asked for and did not get, which is the evidence the question of whether
    /// to build it should be answered from.
    /// </summary>
    public const string BackupDestinationSaved = "BackupDestinationSaved";

    /// <summary>
    /// A stored backup artifact was read back by the agent and found to match its record. Written for
    /// every verification that concluded, after a run and on request alike. Recorded as failed when the
    /// verification could NOT be performed — the agent busy, unreachable or refusing the destination —
    /// because that is a different fact from a finding about the artifact and must not read as one: it
    /// changes no recorded state. The subject is the account's name, never a path, a member name or a
    /// digest (rules/security.md item 8).
    /// </summary>
    public const string BackupVerified = "BackupVerified";

    /// <summary>
    /// The agent read a stored backup artifact back and found it does not match its record: corrupt,
    /// missing or changed. Only a finding about the artifact is written under this action; a
    /// verification that could not run is <see cref="BackupVerified"/> recorded as failed. The subject
    /// is the account's name, and nothing the artifact contained is recorded.
    /// </summary>
    public const string BackupVerificationFailed = "BackupVerificationFailed";
}
