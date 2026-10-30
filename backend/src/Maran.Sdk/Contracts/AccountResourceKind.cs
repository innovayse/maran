namespace Maran.Sdk.Contracts;

/// <summary>
/// A kind of thing an account owns and a module counts: the rows of the account's resource summary.
/// Each value is owned by exactly one module, which is the one that answers for it through
/// <see cref="Interfaces.IAccountResourceCounter"/>.
/// </summary>
public enum AccountResourceKind
{
    /// <summary>The account's sites, counted by the Sites module.</summary>
    Sites,

    /// <summary>The account's databases, counted by the Databases module.</summary>
    Databases,

    /// <summary>The account's SFTP logins, counted by the Sftp module.</summary>
    SftpLogins,

    /// <summary>The account's FTPS logins, counted by the Ftp module.</summary>
    FtpLogins,

    /// <summary>The entries in the account's crontab, counted by the Cron module through the agent.</summary>
    CronEntries,

    /// <summary>The backups the panel holds for the account, counted by the Backups module.</summary>
    Backups,
}
