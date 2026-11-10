namespace Maran.Modules.ServerMigration.Models;

/// <summary>One database of the comparison report: what was loaded for it at the cut-over.</summary>
/// <param name="Name">The database's name here.</param>
/// <param name="DumpBytes">How many bytes of dump were loaded.</param>
public sealed record MigrationDatabaseReport(string Name, long DumpBytes);
