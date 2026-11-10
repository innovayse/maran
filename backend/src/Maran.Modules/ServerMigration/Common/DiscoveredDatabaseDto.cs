namespace Maran.Modules.ServerMigration.Common;

/// <summary>One database found under an account on a remote server.</summary>
/// <param name="Name">The database's name on the source.</param>
/// <param name="SizeBytes">The database's size; meaningful only when <paramref name="SizeKnown"/>.</param>
/// <param name="SizeKnown">Whether the source could measure the database.</param>
public sealed record DiscoveredDatabaseDto(string Name, long SizeBytes, bool SizeKnown);
