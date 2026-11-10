namespace Maran.Modules.ServerMigration.Common;

/// <summary>Where one domain of a job resolves now.</summary>
/// <param name="Domain">The domain.</param>
/// <param name="Addresses">The addresses the system resolver returned.</param>
/// <param name="PointsHere">Whether any of them is an address of this server.</param>
/// <param name="Resolved">Whether the name resolved at all.</param>
public sealed record MigrationDnsRowDto(string Domain, IReadOnlyList<string> Addresses, bool PointsHere, bool Resolved);
