using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.ServerMigration.Commands.DiscardAccountImport;

/// <summary>Discards a staged import without starting it (issue #127).</summary>
/// <param name="ImportId">The import, taken from the route and never from a body.</param>
/// <param name="IpAddress">The caller's address, recorded in the audit journal; read from the connection, never bound.</param>
/// <param name="UserAgent">The caller's user agent, recorded in the audit journal; read from the request, never bound.</param>
public sealed record DiscardAccountImportCommand(
    [property: JsonIgnore][property: BindNever][BindNever] Guid ImportId = default,
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "");
