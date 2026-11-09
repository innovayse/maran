using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Identity.Commands.ResendResellerInvitation;

/// <summary>
/// Resends the invitation to a reseller's contact: retires every outstanding token for the
/// reseller's login and mails a new one.
/// </summary>
/// <param name="ResellerId">
/// The reseller whose login is being (re)invited, taken from the route rather than a body
/// (rules/csharp.md "An endpoint binds its command directly"). Guarded the same way a body-bound
/// server-established field would be: the guard is a property of the type, not of how today's action
/// happens to build it.
/// </param>
/// <param name="IpAddress">The administrator's address, established by the server and stamped by
/// the action. Never bound from a request body.</param>
/// <param name="UserAgent">The administrator's user agent, established by the server and stamped by
/// the action. Never bound from a request body.</param>
public sealed record ResendResellerInvitationCommand(
    [property: JsonIgnore][property: BindNever][BindNever] Guid ResellerId,
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "");
