using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Sites.Commands.SetPhpExtension;

/// <summary>
/// Switches one catalogue PHP extension on or off for one installed PHP version, for every account
/// that uses that version.
/// </summary>
/// <remarks>
/// <b>Server-wide, administrator-only, and said so.</b> One php-fpm master serves every account's pool
/// of a version, so turning an extension off removes it from every site on that version at the reload,
/// and turning one on may install a package through the host's package manager. Like installing a
/// version, it is not one tenant's row; the action is administrator-only and the task it records is
/// no customer's to read (issue #157).
/// </remarks>
/// <param name="Enabled">The state to put the extension in: <see langword="true"/> loaded, <see langword="false"/> not loaded.</param>
/// <param name="Version">
/// The installed PHP version, as the packages name it ("8.3"). It comes from the route and is stamped
/// by the action; a body that carries a <c>version</c> is ignored, so a body value can never disagree
/// with the path.
/// </param>
/// <param name="Extension">
/// The catalogue name of the extension ("gd"), never a package name. It comes from the route and is
/// stamped by the action; the validator refuses anything outside the catalogue, and the agent
/// re-validates it.
/// </param>
/// <param name="IpAddress">
/// The caller's address, recorded in the audit journal. Established by the server and stamped by the
/// action; never bound from the request, which is the thing being audited.
/// </param>
/// <param name="UserAgent">
/// The caller's user agent, recorded in the audit journal. Established by the server and stamped by
/// the action; never bound from the request.
/// </param>
public sealed record SetPhpExtensionCommand(
    bool Enabled,
    [property: JsonIgnore][property: BindNever][BindNever] string Version = "",
    [property: JsonIgnore][property: BindNever][BindNever] string Extension = "",
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "");
