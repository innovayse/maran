namespace Maran.Modules.ServerMigration.Queries.GetAccountImport;

/// <summary>Reads one import.</summary>
/// <param name="ImportId">The import.</param>
public sealed record GetAccountImportQuery(Guid ImportId);
