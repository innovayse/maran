using System.Text.Json.Serialization;
using Maran.Modules.ServerMigration.Models;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.ServerMigration.Commands.DownloadAccountExport;

/// <summary>Writes the package of an export to a stream (issue #127).</summary>
/// <param name="ExportId">The export, taken from the route and never from a body.</param>
/// <param name="Output">
/// Where the package is written, as it is made and without buffering: the response body. Server-established: set
/// by the action and never bound.
/// </param>
/// <param name="IpAddress">The caller's address, recorded in the audit journal; read from the connection, never bound.</param>
/// <param name="UserAgent">The caller's user agent, recorded in the audit journal; read from the request, never bound.</param>
public sealed record DownloadAccountExportCommand(
    [property: JsonIgnore][property: BindNever][BindNever] Guid ExportId = default,
    [property: JsonIgnore][property: BindNever][BindNever] PackageDestination Output = null!,
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "");
