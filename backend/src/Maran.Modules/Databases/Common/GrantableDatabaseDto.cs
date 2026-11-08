namespace Maran.Modules.Databases.Common;

/// <summary>A database of the user's account that the user holds no grant on yet.</summary>
/// <param name="Id">The database's identity.</param>
/// <param name="FullName">The database's fully-qualified name.</param>
public sealed record GrantableDatabaseDto(Guid Id, string FullName);
