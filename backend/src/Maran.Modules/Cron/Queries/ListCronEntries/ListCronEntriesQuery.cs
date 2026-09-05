namespace Maran.Modules.Cron.Queries.ListCronEntries;

/// <summary>
/// Lists the scheduled tasks installed in one account's crontab.
/// </summary>
/// <remarks>
/// Unlike every other module's listing, this one TAKES an account. The others take none, because the
/// scope arrives with the caller's token through a context's tenant query filter — and this module
/// has no context and no rows for a filter to scope. The account is therefore named explicitly and
/// resolved through the tenant-scoped directory, which answers null for an account the caller does
/// not own: the resolution does here exactly what the query filter does there.
/// </remarks>
/// <param name="AccountId">The account whose crontab to read.</param>
public sealed record ListCronEntriesQuery(Guid AccountId);
