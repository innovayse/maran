using Maran.Modules.ServerMigration.Domain.Enums;
using Maran.Modules.ServerMigration.Resources;
using Maran.SharedKernel.Utilities.Text;

namespace Maran.Modules.ServerMigration.Domain.Entities;

/// <summary>
/// One source account inside a migration job: what the administrator mapped it to, and how far its transfer has
/// got (issue #197, phase 2).
/// </summary>
/// <remarks>
/// <para>
/// <b>It owns a snapshot of what the discovery found</b> (<see cref="InventoryJson"/>), taken when the job was made,
/// because the discovery is replaced whenever it is run again and a job in progress must not change under itself.
/// </para>
/// <para>
/// <b>The two-phase flow is the state machine.</b> <see cref="MigrationAccountState.Pending"/> to
/// <see cref="MigrationAccountState.Running"/> to <see cref="MigrationAccountState.Synced"/> is the initial sync with
/// the source still serving; <see cref="MigrationAccountState.CuttingOver"/> to
/// <see cref="MigrationAccountState.Completed"/> is the final delta at cut-over. A failure BEFORE the first sync
/// finished rolls the account back (it was created for this and holds nothing yet); a failure after it keeps the
/// account, because deleting a synced account on a hiccup of the final pass would throw away the transfer the
/// administrator waited for.
/// </para>
/// <para>
/// <b>Secrets.</b> The pull grant of a Maran source and the passwords the import generated are encrypted at rest;
/// neither is in any DTO except the one-time claim of the generated passwords.
/// </para>
/// </remarks>
public sealed class MigrationJobAccount
{
    /// <summary>The longest account name.</summary>
    public const int NameMaxLength = 32;

    /// <summary>The longest home path.</summary>
    public const int HomeMaxLength = 4096;

    /// <summary>The longest domain.</summary>
    public const int DomainMaxLength = 253;

    /// <summary>The longest e-mail address.</summary>
    public const int EmailMaxLength = 320;

    /// <summary>The longest language code.</summary>
    public const int LanguageMaxLength = 8;

    /// <summary>The longest stage name.</summary>
    public const int StageMaxLength = 64;

    /// <summary>The longest failure code.</summary>
    public const int FailureCodeMaxLength = 128;

    /// <summary>The row's identifier.</summary>
    public Guid Id { get; private set; }

    /// <summary>The job the row belongs to.</summary>
    public Guid JobId { get; private set; }

    /// <summary>The account's system user name on the source; it keeps it here (decision D1).</summary>
    public string SourceAccountName { get; private set; }

    /// <summary>The account's home on the source.</summary>
    public string SourceHome { get; private set; }

    /// <summary>The size of the home as the discovery measured it, or <c>null</c> when it could not.</summary>
    public long? SourceDiskBytes { get; private set; }

    /// <summary>How many crontab entries the discovery counted; they are not transferred.</summary>
    public int CronEntryCount { get; private set; }

    /// <summary>How many mailboxes the discovery counted; they are not transferred (decision D16).</summary>
    public int MailboxCount { get; private set; }

    /// <summary>The sites and databases the discovery found, as JSON.</summary>
    public string InventoryJson { get; private set; }

    /// <summary>The plan the account is created against here, once mapped.</summary>
    public Guid? PlanId { get; private set; }

    /// <summary>The reseller that owns the account, or <c>null</c> for the administrator's own.</summary>
    public Guid? TargetResellerId { get; private set; }

    /// <summary>The owner's e-mail address, which a source cannot say.</summary>
    public string? OwnerEmail { get; private set; }

    /// <summary>The owner's language.</summary>
    public string OwnerLanguage { get; private set; }

    /// <summary>The account's primary domain here.</summary>
    public string? PrimaryDomain { get; private set; }

    /// <summary>For a Maran source: the single-use grant that pulls this account's package; encrypted at rest.</summary>
    public string? PullGrant { get; private set; }

    /// <summary>Where the account is.</summary>
    public MigrationAccountState State { get; private set; }

    /// <summary>The step the engine is at, for the progress screen.</summary>
    public string? Stage { get; private set; }

    /// <summary>The code of what stopped the account.</summary>
    public string? FailureCode { get; private set; }

    /// <summary>The account created here, once it exists.</summary>
    public Guid? TargetAccountId { get; private set; }

    /// <summary>
    /// Whether every importer finished: the account, its databases and its sites exist here. A paused or restarted
    /// account that has an account but is not provisioned is rolled back and started over, because a half-created
    /// account cannot be told from a finished one by looking at it.
    /// </summary>
    public bool Provisioned { get; private set; }

    /// <summary>How many rsync runs the last pull took.</summary>
    public int SlicesCompleted { get; private set; }

    /// <summary>The size of the source home measured by the last pull, or <c>null</c>.</summary>
    public long? SourceBytes { get; private set; }

    /// <summary>How many databases have been loaded by the last pass.</summary>
    public int DatabasesLoaded { get; private set; }

    /// <summary>How many bytes of dump the last pass loaded.</summary>
    public long DumpBytes { get; private set; }

    /// <summary>When the engine first started the account.</summary>
    public DateTimeOffset? StartedAt { get; private set; }

    /// <summary>When the initial sync finished.</summary>
    public DateTimeOffset? SyncedAt { get; private set; }

    /// <summary>When the cut-over finished.</summary>
    public DateTimeOffset? CutOverAt { get; private set; }

    /// <summary>When the account ended in a state that is not a pause.</summary>
    public DateTimeOffset? FinishedAt { get; private set; }

    /// <summary>The comparison report of the cut-over, as JSON.</summary>
    public string? ReportJson { get; private set; }

    /// <summary>The credentials the import generated, as JSON, encrypted at rest, until the administrator claims them once.</summary>
    public string? IssuedSecretsJson { get; private set; }

    /// <summary>Whether the plan, the owner and the primary domain are all chosen.</summary>
    public bool IsMapped
    {
        get
        {
            return PlanId is not null
                && !string.IsNullOrWhiteSpace(OwnerEmail)
                && !string.IsNullOrWhiteSpace(PrimaryDomain);
        }
    }

    /// <summary>Whether the account's transfer has reached a state nothing moves it out of by itself.</summary>
    public bool HasEnded
    {
        get
        {
            return State is MigrationAccountState.Completed
                or MigrationAccountState.Failed
                or MigrationAccountState.Cancelled
                or MigrationAccountState.RolledBack
                or MigrationAccountState.RolledBackIncomplete;
        }
    }

    /// <summary>Adds an account to a job as pending and not yet mapped.</summary>
    /// <param name="id">The row's identifier.</param>
    /// <param name="jobId">The job.</param>
    /// <param name="sourceAccountName">The account's system user name on the source.</param>
    /// <param name="sourceHome">The account's home on the source.</param>
    /// <param name="sourceDiskBytes">The size the discovery measured, or <c>null</c>.</param>
    /// <param name="cronEntryCount">How many crontab entries were counted.</param>
    /// <param name="mailboxCount">How many mailboxes were counted.</param>
    /// <param name="inventoryJson">The sites and databases found.</param>
    /// <param name="primaryDomain">The first domain found, as the default primary domain, or <c>null</c>.</param>
    public MigrationJobAccount(
        Guid id,
        Guid jobId,
        string sourceAccountName,
        string sourceHome,
        long? sourceDiskBytes,
        int cronEntryCount,
        int mailboxCount,
        string inventoryJson,
        string? primaryDomain)
    {
        ArgumentNullException.ThrowIfNull(sourceAccountName);
        ArgumentNullException.ThrowIfNull(sourceHome);
        ArgumentNullException.ThrowIfNull(inventoryJson);

        Id = id;
        JobId = jobId;
        SourceAccountName = ColumnText.Fit(sourceAccountName, NameMaxLength);
        SourceHome = ColumnText.Fit(sourceHome, HomeMaxLength);
        SourceDiskBytes = sourceDiskBytes;
        CronEntryCount = cronEntryCount;
        MailboxCount = mailboxCount;
        InventoryJson = inventoryJson;
        PrimaryDomain = primaryDomain is null ? null : ColumnText.Fit(primaryDomain, DomainMaxLength);
        OwnerLanguage = "en";
        State = MigrationAccountState.Pending;
    }

    /// <summary>Required by EF Core only.</summary>
    private MigrationJobAccount()
    {
        SourceAccountName = string.Empty;
        SourceHome = string.Empty;
        InventoryJson = string.Empty;
        OwnerLanguage = "en";
    }

    /// <summary>Changes what the account is mapped to; a value left <c>null</c> keeps what was there.</summary>
    /// <param name="planId">The plan, or <c>null</c> to keep.</param>
    /// <param name="resellerId">The reseller, or <c>null</c> to keep.</param>
    /// <param name="clearReseller">Whether the account becomes the administrator's own instead.</param>
    /// <param name="ownerEmail">The owner's e-mail, or <c>null</c> to keep.</param>
    /// <param name="ownerLanguage">The owner's language, or <c>null</c> to keep.</param>
    /// <param name="primaryDomain">The primary domain, or <c>null</c> to keep.</param>
    /// <param name="pullGrant">For a Maran source, the grant, or <c>null</c> to keep.</param>
    public void Remap(
        Guid? planId,
        Guid? resellerId,
        bool clearReseller,
        string? ownerEmail,
        string? ownerLanguage,
        string? primaryDomain,
        string? pullGrant)
    {
        PlanId = planId ?? PlanId;
        TargetResellerId = clearReseller ? null : resellerId ?? TargetResellerId;
        OwnerEmail = ownerEmail is null ? OwnerEmail : ColumnText.Fit(ownerEmail, EmailMaxLength);
        OwnerLanguage = ownerLanguage is null ? OwnerLanguage : ColumnText.Fit(ownerLanguage, LanguageMaxLength);
        PrimaryDomain = primaryDomain is null ? PrimaryDomain : ColumnText.Fit(primaryDomain, DomainMaxLength);
        PullGrant = pullGrant ?? PullGrant;
    }

    /// <summary>Starts the account: the scheduler picked it.</summary>
    /// <param name="now">When it starts.</param>
    public void BeginRun(DateTimeOffset now)
    {
        State = MigrationAccountState.Running;
        Stage = null;
        FailureCode = null;
        StartedAt ??= now;
        FinishedAt = null;
    }

    /// <summary>Records the step the engine is at.</summary>
    /// <param name="stage">The step's name.</param>
    public void AtStage(string stage)
    {
        ArgumentNullException.ThrowIfNull(stage);

        Stage = ColumnText.Fit(stage, StageMaxLength);
    }

    /// <summary>Records the account the import created, before anything else is done to it.</summary>
    /// <param name="accountId">The account here.</param>
    public void AccountCreated(Guid accountId)
    {
        TargetAccountId = accountId;
    }

    /// <summary>Records that the half-created account was deleted again and the account starts over.</summary>
    public void AccountDeleted()
    {
        TargetAccountId = null;
        Provisioned = false;
        IssuedSecretsJson = null;
    }

    /// <summary>Records that every importer finished.</summary>
    public void Provision()
    {
        Provisioned = true;
    }

    /// <summary>Records the progress of a pull.</summary>
    /// <param name="slices">How many rsync runs have ended.</param>
    public void PullProgressed(int slices)
    {
        SlicesCompleted = slices;
    }

    /// <summary>Records that a pull finished.</summary>
    /// <param name="slices">How many rsync runs it took.</param>
    /// <param name="sourceBytes">The measured size of the source home, or <c>null</c>.</param>
    public void FilesPulled(int slices, long? sourceBytes)
    {
        SlicesCompleted = slices;
        SourceBytes = sourceBytes;
    }

    /// <summary>Starts counting databases for a pass.</summary>
    public void DatabasePassStarted()
    {
        DatabasesLoaded = 0;
        DumpBytes = 0;
    }

    /// <summary>Records one database loaded.</summary>
    /// <param name="dumpBytes">How many bytes of dump it was.</param>
    public void DatabaseLoaded(long dumpBytes)
    {
        DatabasesLoaded++;
        DumpBytes += dumpBytes;
    }

    /// <summary>The initial sync finished: the source keeps serving and the account waits for the cut-over.</summary>
    /// <param name="now">When it finished.</param>
    public void Synced(DateTimeOffset now)
    {
        State = MigrationAccountState.Synced;
        Stage = null;
        SyncedAt = now;
    }

    /// <summary>The administrator asked for the cut-over.</summary>
    /// <returns>Success, or the refusal when the account is not synced.</returns>
    public Result BeginCutOver()
    {
        if (State != MigrationAccountState.Synced)
        {
            return StateInvalid();
        }

        State = MigrationAccountState.CuttingOver;
        Stage = null;
        FailureCode = null;

        return Result.Ok();
    }

    /// <summary>The cut-over finished and was verified.</summary>
    /// <param name="reportJson">The comparison report.</param>
    /// <param name="now">When it finished.</param>
    public void Complete(string reportJson, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(reportJson);

        State = MigrationAccountState.Completed;
        Stage = null;
        ReportJson = reportJson;
        CutOverAt = now;
        FinishedAt = now;
    }

    /// <summary>The final delta or the verification failed; the account is kept.</summary>
    /// <param name="failureCode">The code of what stopped it.</param>
    /// <param name="now">When it stopped.</param>
    public void FailedAfterSync(string failureCode, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(failureCode);

        State = MigrationAccountState.Failed;
        FailureCode = ColumnText.Fit(failureCode, FailureCodeMaxLength);
        FinishedAt = now;
    }

    /// <summary>A pause stopped the account at a boundary; its partial state is kept.</summary>
    public void Paused()
    {
        State = MigrationAccountState.Paused;
    }

    /// <summary>
    /// The administrator resumed the job: a paused account, or one a restart orphaned, is waiting again. One that had
    /// finished its initial sync goes back to being synced, so that the cut-over is a decision the administrator takes
    /// again rather than a step that resumes by itself.
    /// </summary>
    public void Requeue()
    {
        State = SyncedAt is null ? MigrationAccountState.Pending : MigrationAccountState.Synced;
    }

    /// <summary>The account failed before its first sync finished and the account it created was deleted again.</summary>
    /// <param name="failureCode">The code of what stopped it.</param>
    /// <param name="now">When it ended.</param>
    public void RollBack(string failureCode, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(failureCode);

        State = MigrationAccountState.RolledBack;
        FailureCode = ColumnText.Fit(failureCode, FailureCodeMaxLength);
        TargetAccountId = null;
        Provisioned = false;
        IssuedSecretsJson = null;
        FinishedAt = now;
    }

    /// <summary>The account failed and the rollback could not finish: the account the row names remains.</summary>
    /// <param name="failureCode">The code of what stopped it.</param>
    /// <param name="now">When it ended.</param>
    public void RollbackFailed(string failureCode, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(failureCode);

        State = MigrationAccountState.RolledBackIncomplete;
        FailureCode = ColumnText.Fit(failureCode, FailureCodeMaxLength);
        IssuedSecretsJson = null;
        FinishedAt = now;
    }

    /// <summary>The administrator cancelled the account and the account it created was deleted again.</summary>
    /// <param name="now">When it ended.</param>
    public void Cancelled(DateTimeOffset now)
    {
        State = MigrationAccountState.Cancelled;
        TargetAccountId = null;
        Provisioned = false;
        IssuedSecretsJson = null;
        FinishedAt = now;
    }

    /// <summary>Runs a failed account again.</summary>
    /// <returns>Success, or the refusal when the account is not in a state a retry can start from.</returns>
    public Result Retry()
    {
        switch (State)
        {
            case MigrationAccountState.RolledBack:
                State = MigrationAccountState.Pending;
                SlicesCompleted = 0;
                SourceBytes = null;
                DatabasesLoaded = 0;
                DumpBytes = 0;
                FailureCode = null;
                FinishedAt = null;
                return Result.Ok();

            case MigrationAccountState.Failed:
                State = MigrationAccountState.Synced;
                FailureCode = null;
                FinishedAt = null;
                return Result.Ok();

            default:
                return StateInvalid();
        }
    }

    /// <summary>Holds the credentials the import generated until they are claimed once.</summary>
    /// <param name="issuedSecretsJson">The credentials as JSON; encrypted by the column.</param>
    public void HoldIssuedSecrets(string issuedSecretsJson)
    {
        ArgumentNullException.ThrowIfNull(issuedSecretsJson);

        IssuedSecretsJson = issuedSecretsJson;
    }

    /// <summary>Hands the held credentials over once and forgets them.</summary>
    /// <returns>The credentials as JSON, or <c>null</c> when there are none (never issued, or already claimed).</returns>
    public string? ClaimIssuedSecrets()
    {
        var held = IssuedSecretsJson;
        IssuedSecretsJson = null;

        return held;
    }

    /// <summary>The refusal of a transition the account's state does not allow.</summary>
    /// <returns>The failed result.</returns>
    private static Result StateInvalid()
    {
        return Result.Fail(Error.Of(nameof(ErrorMessages.MigrationAccountStateInvalid), ErrorType.Conflict));
    }
}
