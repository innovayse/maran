namespace Maran.Sdk.Contracts;

/// <summary>
/// The machine-stable audit action names for cron entry and environment actions. Constants rather than an enum: a marketplace
/// module records actions this assembly was never compiled knowing about, and an enum could not be
/// extended from outside.
/// </summary>
/// <remarks>
/// One of the Sdk's <c>*AuditActions</c> classes, which together are the closed set of actions the
/// shipped modules record; each is named for the area whose journal writes its constants.
/// </remarks>
public static class CronAuditActions
{
    /// <summary>A scheduled command was added to an account's crontab.</summary>
    /// <remarks>
    /// The subject is the entry's identifier, NEVER the command. A cron command is the customer's
    /// own and is shown back to them in the panel, but it can legitimately carry a credential —
    /// <c>mysql -pSECRET</c>, a URL with a token — and this journal is append-only and never
    /// deleted. An identifier is enough to find the entry; a command here is a customer's password
    /// kept forever, in a place their operator reads.
    /// </remarks>
    public const string CronEntryCreated = "CronEntryCreated";

    /// <summary>A cron entry's schedule or command was replaced. Subject: the entry id, never the command.</summary>
    public const string CronEntryUpdated = "CronEntryUpdated";

    /// <summary>A cron entry was removed, together with the files that held its command and its last run.</summary>
    public const string CronEntryDeleted = "CronEntryDeleted";

    /// <summary>
    /// A cron entry was enabled or disabled. Recorded separately from an update because a disabled
    /// entry that still fires — or an enabled one that does not — is the failure an operator needs
    /// to be able to date.
    /// </summary>
    public const string CronEntryEnabledChanged = "CronEntryEnabledChanged";

    /// <summary>An account's cron environment variables were replaced. Names are recorded; values are not.</summary>
    public const string CronEnvironmentChanged = "CronEnvironmentChanged";

    /// <summary>
    /// A cron entry was started by hand (issue #158). The entry id is recorded; the command and what it
    /// printed are not.
    /// </summary>
    public const string CronEntryRun = "CronEntryRun";
}
