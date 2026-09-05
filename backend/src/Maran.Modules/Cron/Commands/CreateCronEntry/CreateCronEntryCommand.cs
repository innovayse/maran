using Maran.Modules.Cron.Common;
using Maran.Modules.Cron.Services;

namespace Maran.Modules.Cron.Commands.CreateCronEntry;

/// <summary>
/// Installs one scheduled task in an account's crontab (spec §11). The agent mints the entry's
/// identifier; nothing here names one, because an id a caller could choose is an id a caller could
/// point at another entry.
/// </summary>
/// <param name="AccountId">
/// The account whose crontab gains the entry, named by ROW ID rather than by system user name. The
/// handler resolves it to a user name through the tenant-scoped account directory, so an id
/// belonging to somebody else is answered "not found" — the resolution IS the tenant boundary here,
/// because this module has no rows for a query filter to scope.
/// </param>
/// <param name="Schedule">When the entry is to run.</param>
/// <param name="Command">
/// The command line to install, verbatim. Never journalled and never logged: it is the customer's
/// own text and can carry a credential (<see cref="CronAuditJournal"/>).
/// </param>
/// <param name="IpAddress">The caller's address, recorded in the audit journal.</param>
/// <param name="UserAgent">The caller's user agent, recorded in the audit journal.</param>
public sealed record CreateCronEntryCommand(
    Guid AccountId,
    CronScheduleDto Schedule,
    string Command,
    string IpAddress,
    string UserAgent);
