namespace Maran.Modules.ServerMigration.Common;

/// <summary>One account found on a remote server, as the list shows it.</summary>
/// <param name="Id">The discovered account's identifier.</param>
/// <param name="Name">The account's login on the source.</param>
/// <param name="Home">The account's home directory on the source.</param>
/// <param name="DiskBytes">The space the home directory uses; meaningful only when <paramref name="DiskKnown"/>.</param>
/// <param name="DiskKnown">Whether the source could measure the home directory.</param>
/// <param name="SiteCount">How many sites the account serves.</param>
/// <param name="DatabaseCount">How many databases the account owns.</param>
/// <param name="CronEntryCount">How many scheduled jobs the account has.</param>
/// <param name="MailboxCount">How many mailboxes the account holds; mail is counted and never migrated.</param>
/// <param name="Domains">The domains the account's sites answer to, aliases included.</param>
public sealed record DiscoveredAccountDto(
    Guid Id,
    string Name,
    string Home,
    long DiskBytes,
    bool DiskKnown,
    int SiteCount,
    int DatabaseCount,
    int CronEntryCount,
    int MailboxCount,
    IReadOnlyList<string> Domains);
