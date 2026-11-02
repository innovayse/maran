using System.Text.Json.Serialization;
using Maran.Sdk.Contracts;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Identity.Commands.InviteSubUser;

/// <summary>
/// Adds a person to the caller's account with a limited set of features and mails them an invitation
/// to choose their own password.
/// </summary>
/// <param name="Username">The name the new person will sign in with.</param>
/// <param name="Email">The address the invitation is sent to.</param>
/// <param name="Features">
/// The single features the person may use. May be empty (the person reaches nothing the grants gate);
/// each must be one real feature, never a combined set.
/// </param>
/// <param name="IpAddress">The owner's address, established by the server and stamped by the action.
/// Never bound from a request body.</param>
/// <param name="UserAgent">The owner's user agent, established by the server and stamped by the action.
/// Never bound from a request body.</param>
public sealed record InviteSubUserCommand(
    string Username,
    string Email,
    IReadOnlyList<AccountFeature> Features,
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "");
