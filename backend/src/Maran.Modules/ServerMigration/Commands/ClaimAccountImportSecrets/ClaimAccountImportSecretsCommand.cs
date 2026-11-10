using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.ServerMigration.Commands.ClaimAccountImportSecrets;

/// <summary>
/// Returns, once, the credentials an import generated for the databases and logins it created (issue #127,
/// decision D3).
/// </summary>
/// <param name="ImportId">The import, taken from the route and never from a body.</param>
public sealed record ClaimAccountImportSecretsCommand(
    [property: JsonIgnore][property: BindNever][BindNever] Guid ImportId = default);
