using System.Text.Json.Serialization;
using Maran.SharedKernel.Security;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.ServerMigration.Commands.PullAccountImport;

/// <summary>
/// Pulls a package from another panel with its grant and stages it exactly as an uploaded one is (issue #127).
/// Spends the grant's single read of the artifact.
/// </summary>
/// <param name="SourceUrl">The other panel's origin, <c>https://host</c> or <c>https://host:port</c>.</param>
/// <param name="Grant">The grant the exporting administrator issued. A <see cref="SensitiveString"/>, so this record's <c>ToString()</c> never prints it.</param>
/// <param name="ExpectedKeyFingerprint">The fingerprint of the exporting server's key, as read on that server.</param>
/// <param name="IpAddress">The caller's address, recorded in the audit journal; read from the connection, never bound.</param>
/// <param name="UserAgent">The caller's user agent, recorded in the audit journal; read from the request, never bound.</param>
public sealed record PullAccountImportCommand(
    string SourceUrl,
    SensitiveString Grant,
    string ExpectedKeyFingerprint,
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "");
