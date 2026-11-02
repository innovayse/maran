using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Cron.Commands.RunCronEntry;

/// <summary>Starts one cron entry now, as its account, and records the run (issue #158).</summary>
/// <remarks>
/// Bound directly by <c>POST /api/v1/cron-entries/{entryId}/runs</c> (rules/csharp.md "Server-established
/// members of a command"). Nothing about what runs travels in it: the entry id names an entry the agent
/// installed, and the agent runs that entry's own command file.
/// </remarks>
/// <param name="AccountId">
/// The account whose crontab holds the entry, named by row id and resolved in the handler. The
/// resolution is the tenant boundary: another tenant's id is answered "not found".
/// </param>
/// <param name="EntryId">The agent's identifier for the entry to run, established by the server from
/// the route and stamped by the action. Never bound from the request body.</param>
/// <param name="IpAddress">The caller's address, established by the server from the connection and
/// stamped by the action. Never bound from the request body.</param>
/// <param name="UserAgent">The caller's user agent, established by the server from the request
/// headers and stamped by the action. Never bound from the request body.</param>
public sealed record RunCronEntryCommand(
    Guid AccountId,
    [property: JsonIgnore][property: BindNever][BindNever] string EntryId = "",
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "");
