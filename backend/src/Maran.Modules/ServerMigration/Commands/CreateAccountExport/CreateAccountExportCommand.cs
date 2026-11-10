using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.ServerMigration.Commands.CreateAccountExport;

/// <summary>
/// Exports one account for another Maran server: takes a backup to export from and gathers what every module
/// would hand over (issue #127). Administrator only.
/// </summary>
/// <param name="AccountId">The account to export.</param>
/// <param name="IpAddress">The caller's address, recorded in the audit journal; read from the connection, never bound.</param>
/// <param name="UserAgent">The caller's user agent, recorded in the audit journal; read from the request, never bound.</param>
public sealed record CreateAccountExportCommand(
    Guid AccountId,
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "");
