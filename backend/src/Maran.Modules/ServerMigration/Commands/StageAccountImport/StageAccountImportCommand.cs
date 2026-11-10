using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.ServerMigration.Commands.StageAccountImport;

/// <summary>
/// Accepts an uploaded package: verifies its signature against the key fingerprint the administrator
/// confirmed, hands the artifact to the agent, and records the import (issue #127). Nothing about any account
/// changes.
/// </summary>
/// <param name="ExpectedKeyFingerprint">
/// The fingerprint of the sending server's key, as the administrator read it on that server: the lowercase
/// hexadecimal SHA-256 of its public point. An import without one is refused, because a signature alone proves
/// only who signed.
/// </param>
/// <param name="Package">
/// The upload, read as a stream and never buffered. Server-established: set by the action from the request body
/// and never bound from JSON, the query or a form.
/// </param>
/// <param name="IpAddress">The caller's address, recorded in the audit journal; read from the connection, never bound.</param>
/// <param name="UserAgent">The caller's user agent, recorded in the audit journal; read from the request, never bound.</param>
public sealed record StageAccountImportCommand(
    string ExpectedKeyFingerprint,
    [property: JsonIgnore][property: BindNever][BindNever] Stream Package = null!,
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "");
