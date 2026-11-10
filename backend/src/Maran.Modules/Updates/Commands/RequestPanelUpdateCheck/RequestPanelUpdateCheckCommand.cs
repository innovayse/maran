using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Updates.Commands.RequestPanelUpdateCheck;

/// <summary>
/// Asks the root check service to fetch the signed release manifest and record what it names (issue
/// #516). It carries nothing the caller chooses: no channel, address or path exists to send, and the
/// check is whatever the fixed <c>maran update --check-requested</c> does (rules/security.md items 1
/// and 12).
/// </summary>
/// <param name="IpAddress">
/// The caller's address, for the audit journal. Server-established: <c>JsonIgnore</c> closes the JSON
/// body and the bare <c>BindNever</c> closes the query string, the form and the route
/// (rules/csharp.md "Server-established members of a command").
/// </param>
/// <param name="UserAgent">The caller's user agent, for the audit journal; guarded as <paramref name="IpAddress"/> is.</param>
public sealed record RequestPanelUpdateCheckCommand(
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "");
