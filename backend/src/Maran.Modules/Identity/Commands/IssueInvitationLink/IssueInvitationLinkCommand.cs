using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Identity.Commands.IssueInvitationLink;

/// <summary>
/// Creates a fresh invitation for a hosting account's owner and returns its link to the
/// administrator asking, instead of mailing it.
/// </summary>
/// <param name="AccountId">
/// The hosting account whose owner is being invited, taken from the route rather than a body
/// (rules/csharp.md "An endpoint binds its command directly"). Guarded the same way a body-bound
/// server-established field would be: the guard is a property of the type, not of how today's action
/// happens to build it.
/// </param>
/// <param name="IpAddress">The administrator's address, established by the server and stamped by
/// the action. Never bound from a request body.</param>
/// <param name="UserAgent">The administrator's user agent, established by the server and stamped by
/// the action. Never bound from a request body.</param>
public sealed record IssueInvitationLinkCommand(
    [property: JsonIgnore][property: BindNever][BindNever] Guid AccountId,
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "");
