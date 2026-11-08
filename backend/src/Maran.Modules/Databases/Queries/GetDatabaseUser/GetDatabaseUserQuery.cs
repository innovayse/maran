namespace Maran.Modules.Databases.Queries.GetDatabaseUser;

/// <summary>Reads one database user with the databases a grant may still be added on.</summary>
/// <param name="UserId">The user to read; another tenant's id answers "not found".</param>
public sealed record GetDatabaseUserQuery(Guid UserId);
