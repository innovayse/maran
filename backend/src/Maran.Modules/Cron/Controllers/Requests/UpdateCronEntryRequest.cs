using Maran.Modules.Cron.Common;

namespace Maran.Modules.Cron.Controllers.Requests;

/// <summary>The body of <c>PUT /api/v1/cron-entries/{entryId}</c>.</summary>
/// <remarks>
/// The entry id comes from the route rather than from this body, so one request cannot name two
/// different entries and leave the reader of a log or an audit row guessing which one was meant.
///
/// It carries no enablement flag, matching the command: rewriting what a job runs and switching it
/// back on are separate decisions, and an edit that quietly re-enabled a disabled entry would start
/// a job nobody asked to start.
/// </remarks>
/// <param name="AccountId">The account whose crontab holds the entry.</param>
/// <param name="Schedule">The new schedule.</param>
/// <param name="Command">The new command line, verbatim.</param>
public sealed record UpdateCronEntryRequest(Guid AccountId, CronScheduleDto Schedule, string Command);
