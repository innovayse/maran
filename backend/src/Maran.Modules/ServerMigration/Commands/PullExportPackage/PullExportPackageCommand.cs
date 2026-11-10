using System.Text.Json.Serialization;
using Maran.Modules.ServerMigration.Models;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.ServerMigration.Commands.PullExportPackage;

/// <summary>
/// Serves the whole package of an export to another panel that presents a grant: the head and the artifact,
/// streamed as the agent reads it (issue #127). The grant allows this once.
/// </summary>
/// <param name="GrantId">
/// The grant that authenticated the request. Server-established: read by the action from the claim only the grant
/// handler writes, and never bound from the route, the query, a form or the body.
/// </param>
/// <param name="Output">The response body the package is written to; server-established and never bound.</param>
/// <param name="IpAddress">The caller's address, recorded in the audit journal; read from the connection, never bound.</param>
/// <param name="UserAgent">The caller's user agent, recorded in the audit journal; read from the request, never bound.</param>
public sealed record PullExportPackageCommand(
    [property: JsonIgnore][property: BindNever][BindNever] Guid GrantId = default,
    [property: JsonIgnore][property: BindNever][BindNever] PackageDestination Output = null!,
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "");
