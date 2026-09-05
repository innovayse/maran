namespace Maran.Modules.Cron.Commands.DeleteCronEntry;

/// <summary>
/// Removes one entry from an account's crontab, together with the files that held its command and
/// its last run.
/// </summary>
/// <param name="AccountId">
/// The account whose crontab holds the entry, named by row id and resolved in the handler. The
/// resolution is the tenant boundary: another tenant's id is answered "not found".
/// </param>
/// <param name="EntryId">The agent's identifier for the entry to remove.</param>
/// <param name="IpAddress">The caller's address, recorded in the audit journal.</param>
/// <param name="UserAgent">The caller's user agent, recorded in the audit journal.</param>
public sealed record DeleteCronEntryCommand(
    Guid AccountId,
    string EntryId,
    string IpAddress,
    string UserAgent);
