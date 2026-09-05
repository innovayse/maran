namespace Maran.Modules.Cron.Queries.GetCronEntryOutput;

/// <summary>
/// Reads what one cron entry's most recent run left behind.
/// </summary>
/// <remarks>
/// Its own query rather than a field of the listing, because answering it costs one privileged read
/// per entry: folding it into the listing would turn one call into as many calls as the account has
/// entries, every time a screen opened.
/// </remarks>
/// <param name="AccountId">
/// The account whose crontab holds the entry, named by row id and resolved in the handler. The
/// resolution is the tenant boundary: another tenant's id is answered "not found".
/// </param>
/// <param name="EntryId">The agent's identifier for the entry to read.</param>
public sealed record GetCronEntryOutputQuery(Guid AccountId, string EntryId);
