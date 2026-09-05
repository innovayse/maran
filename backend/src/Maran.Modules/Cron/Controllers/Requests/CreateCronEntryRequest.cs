using Maran.Modules.Cron.Common;

namespace Maran.Modules.Cron.Controllers.Requests;

/// <summary>The body of <c>POST /api/v1/cron-entries</c>.</summary>
/// <remarks>
/// A separate type from the command: the command carries the caller's address and user agent, which
/// are read from the connection and must never be settable by the request that is being audited.
///
/// It has no entry-id field, and none may be added. The agent mints the identifier when it installs
/// the entry, so there is nothing for a caller to choose — and an id a caller could choose is an id
/// a caller could aim at an entry that already exists.
/// </remarks>
/// <param name="AccountId">The account whose crontab gains the entry.</param>
/// <param name="Schedule">When the entry is to run.</param>
/// <param name="Command">The command line to install, verbatim.</param>
public sealed record CreateCronEntryRequest(Guid AccountId, CronScheduleDto Schedule, string Command);
