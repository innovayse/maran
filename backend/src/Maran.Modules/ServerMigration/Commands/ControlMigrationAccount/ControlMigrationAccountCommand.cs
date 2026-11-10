using System.Text.Json.Serialization;
using Maran.Modules.ServerMigration.Domain.Enums;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.ServerMigration.Commands.ControlMigrationAccount;

/// <summary>Retries, cancels or cuts over one account of a job (issue #197, phase 2).</summary>
/// <param name="Action">What to do.</param>
/// <param name="JobId">Server-established: the job, from the route.</param>
/// <param name="AccountId">Server-established: the account row, from the route.</param>
/// <param name="IpAddress">Server-established: the caller's address.</param>
/// <param name="UserAgent">Server-established: the caller's user agent.</param>
public sealed record ControlMigrationAccountCommand(
    MigrationAccountAction Action,
    [property: JsonIgnore][property: BindNever][BindNever] Guid JobId = default,
    [property: JsonIgnore][property: BindNever][BindNever] Guid AccountId = default,
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "");
