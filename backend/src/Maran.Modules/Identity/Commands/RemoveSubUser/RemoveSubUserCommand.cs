using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Identity.Commands.RemoveSubUser;

/// <summary>Ends one sub-user's access to the caller's account.</summary>
/// <param name="UserId">
/// The sub-user's login id, taken from the route rather than a body (rules/csharp.md "An endpoint binds
/// its command directly").
/// </param>
/// <param name="IpAddress">The owner's address, established by the server and stamped by the action.
/// Never bound from a request body.</param>
/// <param name="UserAgent">The owner's user agent, established by the server and stamped by the action.
/// Never bound from a request body.</param>
public sealed record RemoveSubUserCommand(
    [property: JsonIgnore][property: BindNever][BindNever] Guid UserId,
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "");
