namespace Maran.Modules.ServerMigration.Common;

/// <summary>A whole-server migration job as the screens show it (issue #197, phase 2).</summary>
/// <param name="Id">The job.</param>
/// <param name="RemoteServerId">The registered server the accounts come from.</param>
/// <param name="Name">The administrator's label.</param>
/// <param name="State">The job's machine-stable state.</param>
/// <param name="StateName">The state as an operator reads it, in the caller's language.</param>
/// <param name="Concurrency">How many accounts are transferred at once.</param>
/// <param name="BandwidthLimitKib">The most KiB per second a transfer reads, or <c>null</c> for no limit.</param>
/// <param name="SourceUrl">For a Maran source, the origin the packages are pulled from.</param>
/// <param name="AccountCount">How many accounts the job holds.</param>
/// <param name="AccountStates">How many accounts are in each state, by state name.</param>
/// <param name="CreatedAt">When the job was created.</param>
/// <param name="StartedAt">When the engine was started.</param>
/// <param name="FinishedAt">When the job ended.</param>
public sealed record MigrationJobDto(
    Guid Id,
    Guid RemoteServerId,
    string Name,
    string State,
    string StateName,
    int Concurrency,
    int? BandwidthLimitKib,
    string? SourceUrl,
    int AccountCount,
    IReadOnlyDictionary<string, int> AccountStates,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt);
