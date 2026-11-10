using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.ServerMigration.Commands.DryRunMigrationJob;

/// <summary>Judges a job against this server without changing anything (issue #197, phase 2).</summary>
/// <param name="JobId">Server-established: the job, from the route.</param>
/// <param name="IpAddress">Server-established: the caller's address.</param>
/// <param name="UserAgent">Server-established: the caller's user agent.</param>
public sealed record DryRunMigrationJobCommand(
    [property: JsonIgnore][property: BindNever][BindNever] Guid JobId = default,
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "");
