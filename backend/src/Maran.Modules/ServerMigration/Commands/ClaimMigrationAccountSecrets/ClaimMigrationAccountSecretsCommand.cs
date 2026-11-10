using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.ServerMigration.Commands.ClaimMigrationAccountSecrets;

/// <summary>Reads, once, the credentials the migration generated for one account (decision D3) (issue #197, phase 2).</summary>
/// <param name="JobId">Server-established: the job, from the route.</param>
/// <param name="AccountId">Server-established: the account row, from the route.</param>
public sealed record ClaimMigrationAccountSecretsCommand(
    [property: JsonIgnore][property: BindNever][BindNever] Guid JobId = default,
    [property: JsonIgnore][property: BindNever][BindNever] Guid AccountId = default);
