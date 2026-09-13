using System.Text.Json.Serialization;
using Maran.Modules.Sites.Domain.Enums;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Sites.Commands.CreateSite;

/// <summary>
/// Creates a website under an account: its document root, its nginx vhost and — for a PHP backend —
/// its FPM pool, through the agent, and then the row that defines it (spec §11).
/// </summary>
/// <param name="AccountId">The account that will own the site.</param>
/// <param name="Domain">The primary domain the site serves.</param>
/// <param name="BackendType">Which backend serves the site's content.</param>
/// <param name="Aliases">
/// Additional hostnames answered by the same vhost. Optional on the wire, and the default is what
/// makes it optional: <c>[ApiController]</c> infers <c>required</c> for a non-nullable reference-type
/// constructor parameter that has none, which would refuse a body that simply omits the field.
/// </param>
/// <param name="PhpVersion">
/// The installed PHP version to bind to; required when the backend is PHP. Optional on the wire for
/// the reason given on <paramref name="Aliases"/>.
/// </param>
/// <param name="ProxyUpstream">
/// The upstream to forward to; required when the backend is a reverse proxy. Optional on the wire for
/// the reason given on <paramref name="Aliases"/>.
/// </param>
/// <param name="IpAddress">
/// The caller's address, recorded in the audit journal. Established by the server and stamped by the
/// action; never bound from the request, which is the thing being audited.
/// </param>
/// <param name="UserAgent">
/// The caller's user agent, recorded in the audit journal. Established by the server and stamped by
/// the action; never bound from the request.
/// </param>
public sealed record CreateSiteCommand(
    Guid AccountId,
    string Domain,
    SiteBackendType BackendType,
    IReadOnlyList<string> Aliases = null!,
    string PhpVersion = "",
    string ProxyUpstream = "",
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "");
