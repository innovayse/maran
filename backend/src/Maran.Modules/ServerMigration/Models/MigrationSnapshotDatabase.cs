namespace Maran.Modules.ServerMigration.Models;

/// <summary>One database of a source account, as the discovery found it when the job was made.</summary>
/// <param name="Name">The database's name on the source.</param>
/// <param name="SizeBytes">Its size, or <c>null</c> when it was not measured.</param>
public sealed record MigrationSnapshotDatabase(string Name, long? SizeBytes);
