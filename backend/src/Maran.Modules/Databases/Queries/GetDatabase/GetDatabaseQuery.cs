namespace Maran.Modules.Databases.Queries.GetDatabase;

/// <summary>Reads one database.</summary>
/// <param name="DatabaseId">The database to read; another tenant's id answers "not found".</param>
public sealed record GetDatabaseQuery(Guid DatabaseId);
