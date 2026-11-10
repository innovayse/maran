namespace Maran.Agent.Client.Services.TransferService;

/// <summary>One hosting account found on a source, complete.</summary>
/// <param name="Name">The system user name, validated by the agent as an account name.</param>
/// <param name="Home">The home directory on the source. Display only; never used as a path on this host.</param>
/// <param name="DiskBytes">The size of the home in bytes, or <c>null</c> when it was not measured.</param>
/// <param name="Sites">The sites whose document root lies in the account's home.</param>
/// <param name="Databases">The databases the account owns, by name prefix or grant.</param>
/// <param name="CronEntryCount">How many crontab entries the account has.</param>
/// <param name="MailboxCount">How many mailboxes were found for the account's domains. Mail is not migrated.</param>
public sealed record AgentSourceAccount(
    string Name,
    string Home,
    long? DiskBytes,
    IReadOnlyList<AgentSourceSite> Sites,
    IReadOnlyList<AgentSourceDatabase> Databases,
    int CronEntryCount,
    int MailboxCount);
