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
/// <b>Installing a licence does not bind it to this server</b> — see
/// <see cref="Commands.InstallLicence.InstallLicenceCommandHandler"/>'s own remarks and the threat
/// note's §6 for why: <c>FingerprintMismatch</c> is unreachable in this codebase today.
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
