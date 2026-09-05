using Maran.Modules.Cron.Common;
using Maran.Modules.Cron.Services;

namespace Maran.Modules.Cron.Commands.UpdateCronEntry;

/// <summary>
/// Replaces the schedule and the command of one installed cron entry, leaving its enablement exactly
/// as it was.
/// </summary>
/// <remarks>
/// It carries no enablement field, and none may be added. Rewriting what an entry runs and switching
/// it back on are separate decisions, and an update that also carried the flag would silently
/// re-enable a disabled entry whenever a customer edited its command without thinking about it —
/// which is a job that starts running again with no one having asked for it.
/// </remarks>
/// <param name="AccountId">
/// The account whose crontab holds the entry, named by row id and resolved in the handler. The
/// resolution is the tenant boundary: another tenant's id is answered "not found".
/// </param>
/// <param name="EntryId">The agent's identifier for the entry to rewrite.</param>
/// <param name="Schedule">The new schedule.</param>
/// <param name="Command">
/// The new command line, verbatim. Never journalled and never logged (<see cref="CronAuditJournal"/>).
/// </param>
/// <param name="IpAddress">The caller's address, recorded in the audit journal.</param>
/// <param name="UserAgent">The caller's user agent, recorded in the audit journal.</param>
public sealed record UpdateCronEntryCommand(
    Guid AccountId,
    string EntryId,
    CronScheduleDto Schedule,
    string Command,
    string IpAddress,
    string UserAgent);
