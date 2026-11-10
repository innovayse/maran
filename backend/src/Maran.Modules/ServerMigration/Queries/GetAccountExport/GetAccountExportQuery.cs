namespace Maran.Modules.ServerMigration.Queries.GetAccountExport;

/// <summary>Reads one export.</summary>
/// <param name="ExportId">The export.</param>
public sealed record GetAccountExportQuery(Guid ExportId);
