namespace Maran.Modules.ServerMigration.Common;

/// <summary>One source account of a job: its mapping and its progress (issue #197, phase 2). Carries no secret.</summary>
/// <param name="Id">The row.</param>
/// <param name="SourceAccountName">The account's system user name; it is kept (decision D1).</param>
/// <param name="SourceHome">The account's home on the source.</param>
/// <param name="SourceDiskBytes">The size the discovery measured, or <c>null</c>.</param>
/// <param name="SiteCount">How many sites the discovery found.</param>
/// <param name="DatabaseCount">How many databases the discovery found.</param>
/// <param name="CronEntryCount">How many crontab entries were counted (not transferred).</param>
/// <param name="MailboxCount">How many mailboxes were counted (not transferred).</param>
/// <param name="PlanId">The plan, once mapped.</param>
/// <param name="ResellerId">The reseller that owns the account.</param>
/// <param name="OwnerEmail">The owner's e-mail.</param>
/// <param name="OwnerLanguage">The owner's language.</param>
/// <param name="PrimaryDomain">The primary domain here.</param>
/// <param name="HasPullGrant">Whether a pull grant is held (a Maran source); the grant itself is never returned.</param>
/// <param name="State">The machine-stable state.</param>
/// <param name="StateName">The state as an operator reads it, in the caller's language.</param>
/// <param name="Stage">The step the engine is at.</param>
/// <param name="FailureCode">The code of what stopped the account.</param>
/// <param name="FailureName">That code as a sentence in the caller's language.</param>
/// <param name="TargetAccountId">The account created here.</param>
/// <param name="SlicesCompleted">How many rsync runs the last pull took.</param>
/// <param name="DatabasesLoaded">How many databases the last pass loaded.</param>
/// <param name="DumpBytes">How many bytes of dump the last pass loaded.</param>
/// <param name="HasIssuedSecrets">Whether generated credentials wait to be claimed once.</param>
/// <param name="SyncedAt">When the initial sync finished.</param>
/// <param name="CutOverAt">When the cut-over finished.</param>
public sealed record MigrationJobAccountDto(
    Guid Id,
    string SourceAccountName,
    string SourceHome,
    long? SourceDiskBytes,
    int SiteCount,
    int DatabaseCount,
    int CronEntryCount,
    int MailboxCount,
    Guid? PlanId,
    Guid? ResellerId,
    string? OwnerEmail,
    string OwnerLanguage,
    string? PrimaryDomain,
    bool HasPullGrant,
    string State,
    string StateName,
    string? Stage,
    string? FailureCode,
    string? FailureName,
    Guid? TargetAccountId,
    int SlicesCompleted,
    int DatabasesLoaded,
    long DumpBytes,
    bool HasIssuedSecrets,
    DateTimeOffset? SyncedAt,
    DateTimeOffset? CutOverAt);
