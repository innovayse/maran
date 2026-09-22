using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Licensing.Commands.InstallLicence;

/// <summary>
/// Installs or replaces the licence artefact (spec §228's write half; see
/// <c>docs/superpowers/notes/2026-09-22-licence-installation-threat-note.md</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Carries the raw licence envelope text, and nothing else the caller controls.</b> There is no
/// separate DTO between the request body and this record: the endpoint accepts the licence text
/// itself, the same bytes <c>Services.LicenceVerifier.VerifyAsync</c> already expects.
/// </para>
/// <para>
/// <b>Whether installing a licence binds it to this server is the licence's decision, not this
/// command's</b>: a payload carrying a <c>server</c> claim is refused on every host but the one it
/// names, and one without it installs anywhere. See
/// <see cref="Commands.InstallLicence.InstallLicenceCommandHandler"/>'s own remarks, and
/// <c>LicenceServerBindingPolicy</c> for why an unreadable host identity refuses rather than
/// allows.
/// </para>
/// </remarks>
/// <param name="RawLicenceText">The uploaded licence envelope's raw text, to be verified before anything is persisted.</param>
/// <param name="IpAddress">
/// The caller's address, for the operator-facing record of who ran this. Established by the server
/// and stamped by the controller; never bound from the request.
/// </param>
/// <param name="UserAgent">The caller's user agent, for the same record. Established by the server and stamped by the controller.</param>
public sealed record InstallLicenceCommand(
    string RawLicenceText,
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "");
