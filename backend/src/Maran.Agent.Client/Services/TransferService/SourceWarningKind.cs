namespace Maran.Agent.Client.Services.TransferService;

/// <summary>
/// The warnings an inventory read can raise. A closed set: the panel renders each from its own
/// language table, never from the agent's words.
/// </summary>
public enum SourceWarningKind
{
    /// <summary>A name did not validate and the entry was skipped.</summary>
    NameRefused = 0,

    /// <summary>A virtual host file could not be parsed and was skipped.</summary>
    SiteUnreadable = 1,

    /// <summary>A crontab could not be read.</summary>
    CronUnreadable = 2,

    /// <summary>The databases could not be read: no database server, or access refused.</summary>
    DatabasesUnreadable = 3,

    /// <summary>A home could not be measured.</summary>
    DiskUnreadable = 4,

    /// <summary>A bound was hit (too many accounts, lines or bytes) and the read is incomplete.</summary>
    Truncated = 5,

    /// <summary>Mail was found on the source and is not migrated.</summary>
    MailNotMigrated = 6,
}
