using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Identity.Commands.ResendSubUserInvitation;

/// <summary>
/// Sends a sub-user's invitation again, on behalf of the owner of the account the sub-user belongs
/// to: retires every outstanding token of the pending login and mails a new one to the address
/// already stored on it.
/// </summary>
/// <param name="UserId">
/// The pending sub-user's login id, taken from the route rather than a body (rules/csharp.md "An
/// endpoint binds its command directly"), so the URL cannot disagree with the body. There is no
/// address in this command on purpose: the mail goes to the one stored on the login, never to a
/// value typed in the request.
/// </param>
/// <param name="IpAddress">The owner's address, established by the server and stamped by the action.
/// Never bound from a request body.</param>
/// <param name="UserAgent">The owner's user agent, established by the server and stamped by the action.
/// Never bound from a request body.</param>
public sealed record ResendSubUserInvitationCommand(
    [property: JsonIgnore][property: BindNever][BindNever] Guid UserId = default,
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "");
