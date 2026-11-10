using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.ServerMigration.Commands.SetMigrationLimits;

/// <summary>Changes how many accounts a job transfers at once and how fast, while it runs (issue #197, phase 2; decision D20).</summary>
/// <param name="Concurrency">How many accounts at once, 1 to 8.</param>
/// <param name="BandwidthLimitKib">The most KiB per second a transfer reads, or <c>null</c> for no limit.</param>
/// <param name="JobId">Server-established: the job, from the route.</param>
/// <param name="IpAddress">Server-established: the caller's address.</param>
/// <param name="UserAgent">Server-established: the caller's user agent.</param>
public sealed record SetMigrationLimitsCommand(
    int Concurrency,
    int? BandwidthLimitKib = null,
    [property: JsonIgnore][property: BindNever][BindNever] Guid JobId = default,
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "");
