using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Ssl.Commands.IssuePanelCertificate;

/// <summary>
/// Orders a trusted certificate for the panel's own configured host name and installs it as the pair
/// the panel's interface is served with (issue #132). Carries nothing a caller chooses: the host name
/// is the operator's own configuration, never a request field.
/// </summary>
/// <param name="IpAddress">
/// The caller's address, recorded in the audit journal. Established by the server and stamped by the
/// action; never bound from the request.
/// </param>
/// <param name="UserAgent">
/// The caller's user agent, recorded in the audit journal. Established by the server and stamped by
/// the action; never bound from the request.
/// </param>
public sealed record IssuePanelCertificateCommand(
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "");
