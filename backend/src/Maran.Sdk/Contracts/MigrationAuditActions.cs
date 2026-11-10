namespace Maran.Sdk.Contracts;

/// <summary>
/// The machine-stable audit action names for the registry of remote servers an administrator reads from
/// (issue #197) and for moving one account between Maran servers (issue #127). Constants rather than an enum: a marketplace module records actions this assembly was never
/// compiled knowing about, and an enum could not be extended from outside.
/// </summary>
/// <remarks>
/// One of the Sdk's <c>*AuditActions</c> classes, which together are the closed set of actions the shipped modules
/// record; each is named for the area whose journal writes its constants. Every entry's subject is the server's id
/// and host, never a key.
/// </remarks>
public static class MigrationAuditActions
{
    /// <summary>A remote server was registered and a key was generated for it.</summary>
    public const string RemoteServerAdded = "RemoteServerAdded";

    /// <summary>An administrator confirmed a host key by its fingerprint and the panel pinned it.</summary>
    public const string RemoteServerHostKeyPinned = "RemoteServerHostKeyPinned";

    /// <summary>The panel tested its connection to a remote server.</summary>
    public const string RemoteServerTested = "RemoteServerTested";

    /// <summary>A remote server's label, address, port, login or key restriction was changed.</summary>
    public const string RemoteServerEdited = "RemoteServerEdited";

    /// <summary>A remote server was removed from the registry.</summary>
    public const string RemoteServerRemoved = "RemoteServerRemoved";

    /// <summary>The panel read the inventory of a remote server.</summary>
    public const string RemoteServerDiscovered = "RemoteServerDiscovered";

    /// <summary>The panel read the host keys a remote server offers, to be compared and confirmed.</summary>
    public const string RemoteServerHostKeysScanned = "RemoteServerHostKeysScanned";

    /// <summary>An account's package was exported: a backup was made and a signed manifest written (issue #127).</summary>
    public const string AccountExported = "AccountExported";

    /// <summary>An administrator issued a single-use pull grant for one exported package.</summary>
    public const string ExportGrantIssued = "ExportGrantIssued";

    /// <summary>An administrator revoked a pull grant before it was used or expired.</summary>
    public const string ExportGrantRevoked = "ExportGrantRevoked";

    /// <summary>An administrator downloaded an export's package through the panel.</summary>
    public const string ExportDownloaded = "ExportDownloaded";

    /// <summary>A pull grant was presented and the package manifest or artifact was served.</summary>
    public const string ExportGrantPulled = "ExportGrantPulled";

    /// <summary>An export reached the end of its retention: its backup artifact and its record were removed (issue #127).</summary>
    public const string ExportExpired = "ExportExpired";

    /// <summary>An administrator read the head of another panel's package with a grant, to check it before pulling (issue #127).</summary>
    public const string ImportPullPreviewed = "ImportPullPreviewed";

    /// <summary>An administrator pulled a package from another panel with a grant; the staging itself is <see cref="ImportStaged"/>.</summary>
    public const string ImportPulled = "ImportPulled";

    /// <summary>A package was staged for import: its signature verified and the artifact accepted by the agent.</summary>
    public const string ImportStaged = "ImportStaged";

    /// <summary>The dry run of an import was read.</summary>
    public const string ImportPlanned = "ImportPlanned";

    /// <summary>An import started creating the account.</summary>
    public const string ImportStarted = "ImportStarted";

    /// <summary>An import finished and the account is complete.</summary>
    public const string ImportCompleted = "ImportCompleted";

    /// <summary>An import failed and the account it created was deleted again.</summary>
    public const string ImportRolledBack = "ImportRolledBack";

    /// <summary>An import failed and the rollback could not finish; the account it names remains.</summary>
    public const string ImportRollbackIncomplete = "ImportRollbackIncomplete";

    /// <summary>A staged import was discarded without being started.</summary>
    public const string ImportDiscarded = "ImportDiscarded";

    /// <summary>A whole-server migration job was created from a registered server's accounts (issue #197).</summary>
    public const string JobCreated = "MigrationJobCreated";

    /// <summary>The mapping of a job's accounts to plans and owners was changed.</summary>
    public const string JobMapped = "MigrationJobMapped";

    /// <summary>The dry run of a job was read.</summary>
    public const string JobPlanned = "MigrationJobPlanned";

    /// <summary>A job's concurrency or bandwidth limit was changed.</summary>
    public const string JobLimitsChanged = "MigrationJobLimitsChanged";

    /// <summary>A job's engine was started.</summary>
    public const string JobStarted = "MigrationJobStarted";

    /// <summary>A job was paused.</summary>
    public const string JobPaused = "MigrationJobPaused";

    /// <summary>A paused job was resumed.</summary>
    public const string JobResumed = "MigrationJobResumed";

    /// <summary>A job was cancelled and the accounts it had created were deleted again.</summary>
    public const string JobCancelled = "MigrationJobCancelled";

    /// <summary>Every account of a job ended and the job completed.</summary>
    public const string JobCompleted = "MigrationJobCompleted";

    /// <summary>One account's initial sync finished; the source keeps serving.</summary>
    public const string AccountSynced = "MigrationAccountSynced";

    /// <summary>One account was cut over: the final delta ran and the verification passed.</summary>
    public const string AccountCutOver = "MigrationAccountCutOver";

    /// <summary>One account failed before its first sync finished and was deleted again.</summary>
    public const string AccountRolledBack = "MigrationAccountRolledBack";

    /// <summary>One account failed and the rollback could not finish; the account it names remains.</summary>
    public const string AccountRollbackIncomplete = "MigrationAccountRollbackIncomplete";

    /// <summary>One account failed at its cut-over; the account is kept.</summary>
    public const string AccountCutOverFailed = "MigrationAccountCutOverFailed";

    /// <summary>One account was cancelled and the account it created was deleted again.</summary>
    public const string AccountCancelled = "MigrationAccountCancelled";

    /// <summary>A failed account was run again.</summary>
    public const string AccountRetried = "MigrationAccountRetried";

    /// <summary>The credentials a job's import generated for one account were claimed once.</summary>
    public const string AccountSecretsClaimed = "MigrationAccountSecretsClaimed";
}
