using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Sites.Commands.InstallPhpVersion;

/// <summary>
/// Installs a PHP version on this server, so sites can be pointed at it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this lives in the Sites module.</b> PHP has no module of its own, and inventing one for a
/// single operation would put the list of installed versions in one place and the way to add to it
/// in another. Sites is where a version is CHOSEN — <c>CreateSite</c> and
/// <c>ChangeSitePhpVersion</c> both read the installed list and refuse anything outside it — so it
/// is where "the list is too short" is discovered, and where the answer to that belongs.
/// </para>
/// <para>
/// <b>It is server-wide, not per-account, and the authorization says so.</b> Every other command in
/// this module acts on one site belonging to one account. This one changes what every site on the
/// box may be pointed at and adds packages an administrator will be patching for years, so it is
/// administrator-only rather than scoped to a tenant.
/// </para>
/// </remarks>
/// <param name="Version">
/// The version to install, as the agent's package layer names it — "8.3", not "php8.3" and not
/// "8.3.14". It travels in the BODY rather than the path, and deliberately: a path parameter would
/// make this a resource-scoped route, which carries the panel's "another tenant's row answers 404,
/// never 403" rule. A PHP runtime is not one tenant's row to hide — a customer who asks for it
/// should be told they may not, not that it does not exist — so the version is an argument to an
/// action, the same way the sibling change-version command takes one.
/// </param>
/// <param name="IpAddress">
/// The caller's address, recorded in the audit journal. Established by the server and stamped by the
/// action; never bound from the request, which is the thing being audited.
/// </param>
/// <param name="UserAgent">
/// The caller's user agent, recorded in the audit journal. Established by the server and stamped by
/// the action; never bound from the request.
/// </param>
public sealed record InstallPhpVersionCommand(
    string Version,
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "");
