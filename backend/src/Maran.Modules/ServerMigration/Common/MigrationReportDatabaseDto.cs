namespace Maran.Modules.ServerMigration.Common;

/// <summary>One database loaded at the cut-over.</summary>
/// <param name="Name">The database's name here.</param>
/// <param name="DumpBytes">How many bytes of dump were loaded.</param>
public sealed record MigrationReportDatabaseDto(string Name, long DumpBytes);
