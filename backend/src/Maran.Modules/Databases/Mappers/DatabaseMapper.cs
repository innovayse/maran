using Maran.Modules.Databases.Common;
using Maran.Modules.Databases.Domain.Entities;
using Maran.SharedKernel.Security;

namespace Maran.Modules.Databases.Mappers;

/// <summary>Restates a <see cref="Database"/> as the wire shape a screen reads.</summary>
/// <remarks>
/// The one place a <see cref="DatabaseDto"/> and its create answer are built, so the list, the single read
/// and the create answer cannot drift apart. A mapper translates; it never decides
/// (rules/csharp.md): the owner names arrive already resolved by the account directory, and this
/// only picks the row's own entry out of them.
/// </remarks>
public static class DatabaseMapper
{
    /// <summary>Builds the outward view of one database.</summary>
    /// <param name="database">The recorded database.</param>
    /// <param name="ownerNames">
    /// Account names the account directory answered for the rows being mapped, tenant-scoped. An
    /// account it did not name — gone, or not the caller's to see — is absent, and the row's owner
    /// name is then empty.
    /// </param>
    /// <returns>The wire shape carrying exactly what the row holds, and its owner's name.</returns>
    public static DatabaseDto From(Database database, IReadOnlyDictionary<Guid, string> ownerNames)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(ownerNames);

        return new DatabaseDto(
            database.Id,
            database.AccountId,
            ownerNames.GetValueOrDefault(database.AccountId, string.Empty),
            database.Name,
            database.FullName,
            database.DbUserName,
            database.CreatedAt);
    }

    /// <summary>
    /// Builds the one-time answer to creating a database, the only view that carries its password.
    /// </summary>
    /// <param name="database">The recorded database.</param>
    /// <param name="ownerNames">
    /// Account names the account directory answered, tenant-scoped; an account it did not name is
    /// absent and the owner name is then empty, exactly as on every read.
    /// </param>
    /// <param name="password">The generated password, shown this once and kept nowhere.</param>
    /// <returns>The create answer carrying what the row holds, its owner's name and the password.</returns>
    public static CreatedDatabaseDto Created(
        Database database,
        IReadOnlyDictionary<Guid, string> ownerNames,
        SensitiveString password)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(ownerNames);
        ArgumentNullException.ThrowIfNull(password);

        return new CreatedDatabaseDto(
            database.Id,
            database.AccountId,
            ownerNames.GetValueOrDefault(database.AccountId, string.Empty),
            database.Name,
            database.FullName,
            database.DbUserName,
            password,
            database.CreatedAt);
    }
}
