using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Marketplace.Commands.SetMarketplaceEnabled;

/// <summary>
/// Switches the marketplace on or off. The administrator proves their password again first, because
/// switching it on is what lets the panel contact an outside host.
/// </summary>
/// <param name="Enabled">True to switch the marketplace on, false to switch it off.</param>
/// <param name="Password">The administrator's current password.</param>
/// <param name="UserId">
/// The administrator, from their own access token. Server-established: <c>JsonIgnore</c> closes the JSON
/// body and the bare <c>BindNever</c> closes the query string, the form and the route
/// (rules/csharp.md "Server-established members of a command").
/// </param>
/// <param name="IpAddress">The caller's address, for the audit journal; guarded as <paramref name="UserId"/> is.</param>
/// <param name="UserAgent">The caller's user agent, for the audit journal; guarded as <paramref name="UserId"/> is.</param>
public sealed record SetMarketplaceEnabledCommand(
    bool Enabled,
    string Password,
    [property: JsonIgnore][property: BindNever][BindNever] Guid UserId = default,
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "");
