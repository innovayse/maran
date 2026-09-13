using System.Text.Json.Serialization;
using Maran.Modules.Cron.Common;
using Maran.Modules.Cron.Services;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Cron.Commands.CreateCronEntry;

/// <summary>
/// Installs one scheduled task in an account's crontab (spec §11). The agent mints the entry's
/// identifier; nothing here names one, because an id a caller could choose is an id a caller could
/// point at another entry.
/// </summary>
/// <remarks>
/// Bound directly by <c>POST /api/v1/cron-entries</c> — there is no separate request type
/// (rules/csharp.md "Server-established members of a command"). The two members the server
/// establishes are guarded so a body cannot carry them.
/// </remarks>
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
/// <param name="IpAddress">The caller's address, established by the server from the connection and
/// stamped by the action. Never bound from the request body — an audited address a caller could
/// state is not evidence of anything.</param>
/// <param name="UserAgent">The caller's user agent, established by the server from the request
/// headers and stamped by the action. Never bound from the request body.</param>
public sealed record CreateCronEntryCommand(
    Guid AccountId,
    CronScheduleDto Schedule,
    string Command,
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "");
