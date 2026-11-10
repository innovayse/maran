using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.ServerMigration.Commands.CreateMigrationJob;

/// <summary>Creates a whole-server migration job from accounts a registered server's discovery found (issue #197, phase 2).</summary>
/// <param name="ServerId">The registered server.</param>
/// <param name="Name">The administrator's label for the job.</param>
/// <param name="DiscoveredAccountIds">The discovered accounts to migrate.</param>
/// <param name="Concurrency">How many accounts at once, 1 to 8, or <c>null</c> for the default of 2.</param>
/// <param name="BandwidthLimitKib">The most KiB per second a transfer reads, or <c>null</c> for no limit.</param>
/// <param name="SourceUrl">For a Maran source, the https origin of its pull endpoint.</param>
/// <param name="ExpectedKeyFingerprint">For a Maran source, the key fingerprint the administrator confirmed against it.</param>
/// <param name="IpAddress">Server-established: the caller's address.</param>
/// <param name="UserAgent">Server-established: the caller's user agent.</param>
public sealed record CreateMigrationJobCommand(
    Guid ServerId,
    string Name,
    IReadOnlyList<Guid> DiscoveredAccountIds,
    int? Concurrency = null,
    int? BandwidthLimitKib = null,
    string? SourceUrl = null,
    string? ExpectedKeyFingerprint = null,
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "");
