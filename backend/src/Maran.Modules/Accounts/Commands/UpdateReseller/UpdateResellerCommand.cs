using System.Text.Json.Serialization;
using Maran.SharedKernel.Constants;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Accounts.Commands.UpdateReseller;

/// <summary>
/// Renames a reseller and replaces its contact address and language. Bound directly by
/// <c>PUT /api/v1/resellers/{id}</c>, which only an administrator may call; what the reseller was sold
/// is changed through its envelope, not here.
/// </summary>
/// <remarks>
/// The whole identity is sent every time, as the envelope is: the command replaces these three
/// values and never patches one of them. The reseller's id sits with the server-established members
/// at the end, because a guarded non-nullable parameter must carry a default and a C# default is only
/// legal in a trailing position.
/// </remarks>
/// <param name="Name">
/// The reseller's name after the change, which is also its login name: the account name alphabet,
/// not a reserved system name and not a name any account or other reseller holds.
/// </param>
/// <param name="ContactEmail">
/// The address the reseller's login is mailed at, its invitation and its password resets, after the
/// change.
/// </param>
/// <param name="Language">The contact's language, one of <see cref="PanelLanguages.Supported"/>.</param>
/// <param name="ResellerId">
/// The reseller being changed, established by the server from the route and stamped by the action.
/// Never bound from the request — a body id could disagree with the path id, and one request would
/// then name two resellers.
/// </param>
/// <param name="IpAddress">The caller's address, recorded in the audit journal. Never bound from the request.</param>
/// <param name="UserAgent">The caller's user agent, recorded in the audit journal. Never bound from the request.</param>
public sealed record UpdateResellerCommand(
    string Name,
    string ContactEmail,
    string Language,
    [property: JsonIgnore][property: BindNever][BindNever] Guid ResellerId = default,
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "");
