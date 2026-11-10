namespace Maran.Modules.ServerMigration.Common;

/// <summary>What one discovery found.</summary>
/// <param name="DiscoveredAt">When the discovery finished.</param>
/// <param name="AccountCount">How many accounts it found.</param>
/// <param name="Warnings">What it could not read or did not report in full.</param>
public sealed record DiscoveryResultDto(
    DateTimeOffset DiscoveredAt,
    int AccountCount,
    IReadOnlyList<DiscoveryWarningDto> Warnings);
