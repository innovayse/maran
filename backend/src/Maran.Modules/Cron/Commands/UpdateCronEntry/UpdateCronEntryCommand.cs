using System.Text.Json.Serialization;
using Maran.Modules.Cron.Common;
using Maran.Modules.Cron.Services;
using Microsoft.AspNetCore.Mvc.ModelBinding;

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
///
/// Bound directly by <c>PUT /api/v1/cron-entries/{entryId}</c> — there is no separate request type
/// (rules/csharp.md "Server-established members of a command"). <paramref name="EntryId"/> sits with
/// the other server-established members at the end rather than beside
/// <paramref name="AccountId"/> where it reads best: a guarded non-nullable reference-type parameter
/// must carry a default, because MVC infers <c>required</c> for one and answers 400 otherwise, and a
/// C# default is only legal in a trailing position.
/// </remarks>
/// <param name="AccountId">
/// The account whose crontab holds the entry, named by row id and resolved in the handler. The
/// resolution is the tenant boundary: another tenant's id is answered "not found".
/// </param>
/// <param name="Schedule">The new schedule.</param>
/// <param name="Command">
/// The new command line, verbatim. Never journalled and never logged (<see cref="CronAuditJournal"/>).
/// </param>
/// <param name="EntryId">The agent's identifier for the entry to rewrite, established by the server
/// from the route and stamped by the action. Never bound from the request body — a body id could
/// disagree with the path id, and one request would then name two entries.</param>
/// <param name="IpAddress">The caller's address, established by the server from the connection and
/// stamped by the action. Never bound from the request body.</param>
/// <param name="UserAgent">The caller's user agent, established by the server from the request
/// headers and stamped by the action. Never bound from the request body.</param>
public sealed record UpdateCronEntryCommand(
    Guid AccountId,
    CronScheduleDto Schedule,
    string Command,
    [property: JsonIgnore][property: BindNever][BindNever] string EntryId = "",
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "");
