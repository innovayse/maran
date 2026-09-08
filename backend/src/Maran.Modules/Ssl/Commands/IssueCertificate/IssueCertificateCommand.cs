using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Ssl.Commands.IssueCertificate;

/// <summary>
/// Orders a certificate for one of the caller's sites from the configured ACME authority and installs
/// it (spec §11).
/// </summary>
/// <param name="Domain">
/// The domain to issue for. It must be a site the caller owns. Optional on the wire, and the default
/// is what makes it optional: <c>[ApiController]</c> infers <c>required</c> for a non-nullable
/// reference-type constructor parameter that has none, which would answer a body that omits the
/// field with a model-state error instead of this module's own validation code.
/// </param>
/// <param name="IpAddress">
/// The caller's address, recorded in the audit journal. Established by the server and stamped by the
/// action; never bound from the request, which is the thing being audited.
/// </param>
/// <param name="UserAgent">
/// The caller's user agent, recorded in the audit journal. Established by the server and stamped by
/// the action; never bound from the request.
/// </param>
public sealed record IssueCertificateCommand(
    string Domain = "",
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "");
