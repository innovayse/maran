using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.ServerMigration.Commands.ConfirmRemoteServerHostKey;

/// <summary>
/// Pins the host key whose fingerprint the administrator typed, once they have compared it with the one the server's
/// own console shows. The panel connects only to a server whose key matches the pinned one.
/// </summary>
/// <remarks>
/// <b>The match is the backend's decision.</b> The fingerprint is compared exactly — same case, same characters —
/// with the fingerprints the server offered; a selection made in the interface is advice, and a request that names a
/// fingerprint the server never offered pins nothing.
/// </remarks>
/// <param name="Fingerprint">The <c>SHA256:</c> fingerprint to pin, as the administrator confirmed it.</param>
/// <param name="ServerId">
/// The server, from the route. Established by the action and never bound from the body.
/// </param>
/// <param name="IpAddress">
/// The caller's address, recorded in the audit journal. Established by the server and stamped by the action; never
/// bound from the request, which is the thing being audited.
/// </param>
/// <param name="UserAgent">
/// The caller's user agent, recorded in the audit journal. Established by the server and stamped by the action; never
/// bound from the request.
/// </param>
public sealed record ConfirmRemoteServerHostKeyCommand(
    string Fingerprint,
    [property: JsonIgnore][property: BindNever][BindNever] Guid ServerId = default,
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "");
