using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.ServerMigration.Commands.IssueExportGrant;

/// <summary>
/// Issues a single-use credential with which another panel pulls one exported package (issue #127). The token
/// is returned once.
/// </summary>
/// <param name="AllowedFrom">An address range the pull must come from, or <c>null</c> for any address.</param>
/// <param name="LifetimeMinutes">How long the grant works, or <c>null</c> for the default of half an hour; never above an hour.</param>
/// <param name="ExportId">The export, taken from the route and never from a body.</param>
/// <param name="IpAddress">The caller's address, recorded in the audit journal; read from the connection, never bound.</param>
/// <param name="UserAgent">The caller's user agent, recorded in the audit journal; read from the request, never bound.</param>
public sealed record IssueExportGrantCommand(
    string? AllowedFrom = null,
    int? LifetimeMinutes = null,
    [property: JsonIgnore][property: BindNever][BindNever] Guid ExportId = default,
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "");
