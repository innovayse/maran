using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Ssl.Commands.RestorePanelCertificate;

/// <summary>
/// Returns the panel's own interface to a freshly generated self-signed certificate for its
/// configured host name, and forgets the trusted certificate recorded for it (issue #425). Carries
/// nothing a caller chooses: the host name is the operator's own configuration, never a request field.
/// </summary>
/// <param name="IpAddress">
/// The caller's address, recorded in the audit journal. Established by the server and stamped by the
/// action; never bound from the request.
/// </param>
/// <param name="UserAgent">
/// The caller's user agent, recorded in the audit journal. Established by the server and stamped by
/// the action; never bound from the request.
/// </param>
public sealed record RestorePanelCertificateCommand(
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "");
