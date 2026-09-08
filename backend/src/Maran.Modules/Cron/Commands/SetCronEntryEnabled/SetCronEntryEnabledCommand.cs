using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Cron.Commands.SetCronEntryEnabled;

/// <summary>
/// Switches one cron entry on or off without touching its schedule or its command.
/// </summary>
/// <remarks>
/// Its own operation, and its own audit action, rather than a field on the update: a disabled entry
/// that still fires — or an enabled one that does not — is the failure an operator needs to be able
/// to date, and folding the flag into an edit would make that date the date of an unrelated change.
///
/// Disabling keeps the entry in the crontab, commented out, so switching one off never loses it.
///
/// Bound directly by <c>POST /api/v1/cron-entries/{entryId}/enabled</c> — there is no separate
/// request type (rules/csharp.md "Server-established members of a command"). <paramref name="EntryId"/>
/// sits with the other server-established members at the end for the reason
/// <c>UpdateCronEntryCommand</c> states: a guarded non-nullable reference-type parameter needs a
/// default, and a default is only legal in a trailing position.
/// </remarks>
/// <param name="AccountId">
/// The account whose crontab holds the entry, named by row id and resolved in the handler. The
/// resolution is the tenant boundary: another tenant's id is answered "not found".
/// </param>
/// <param name="Enabled">True installs it as a live crontab line; false comments it out.</param>
/// <param name="EntryId">The agent's identifier for the entry to switch, established by the server
/// from the route and stamped by the action. Never bound from the request body — a body id could
/// disagree with the path id, and one request would then name two entries.</param>
/// <param name="IpAddress">The caller's address, established by the server from the connection and
/// stamped by the action. Never bound from the request body.</param>
/// <param name="UserAgent">The caller's user agent, established by the server from the request
/// headers and stamped by the action. Never bound from the request body.</param>
public sealed record SetCronEntryEnabledCommand(
    Guid AccountId,
    bool Enabled,
    [property: JsonIgnore][property: BindNever][BindNever] string EntryId = "",
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "");
