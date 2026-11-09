using Maran.Modules.Databases.Domain.Enums;

namespace Maran.Modules.Databases.Common;

/// <summary>What one database user holds on one database.</summary>
/// <param name="DatabaseId">The database the privileges are held on.</param>
/// <param name="DatabaseFullName">The database's fully-qualified name, as MySQL holds it.</param>
/// <param name="Privileges">The privileges held, in the order the screen lists them; never empty.</param>
public sealed record DatabaseUserGrantDto(
    Guid DatabaseId,
    string DatabaseFullName,
    IReadOnlyList<DatabasePrivilege> Privileges);
