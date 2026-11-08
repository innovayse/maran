using Maran.Modules.Databases.Common;
using Maran.Modules.Databases.Domain.Entities;
using Maran.Modules.Databases.Domain.Policies;
using Maran.SharedKernel.Security;

namespace Maran.Modules.Databases.Mappers;

/// <summary>Translates a <see cref="DatabaseUser"/> and what was read beside it into the wire shapes.</summary>
/// <remarks>A mapper restates; every decision it needs, such as whether the user is dedicated, arrives already made.</remarks>
public static class DatabaseUserMapper
{
    /// <summary>One user as a list row.</summary>
    /// <param name="user">The user, loaded with its grants.</param>
    /// <param name="ownerNames">Account names by id.</param>
    /// <param name="databaseNames">Full database names by id, covering every database the user holds a grant on.</param>
    /// <param name="isDedicated">Whether a database of the account names this user as its dedicated one.</param>
    /// <returns>The wire shape.</returns>
    public static DatabaseUserDto From(
        DatabaseUser user,
        IReadOnlyDictionary<Guid, string> ownerNames,
        IReadOnlyDictionary<Guid, string> databaseNames,
        bool isDedicated)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(ownerNames);
        ArgumentNullException.ThrowIfNull(databaseNames);

        return new DatabaseUserDto(
            user.Id,
            user.AccountId,
            ownerNames.GetValueOrDefault(user.AccountId, string.Empty),
            user.NameSuffix,
            user.FullName,
            isDedicated,
            Grants(user, databaseNames),
            user.CreatedAt);
    }

    /// <summary>One user with the databases a grant may still be added on.</summary>
    /// <param name="user">The user, loaded with its grants.</param>
    /// <param name="ownerNames">Account names by id.</param>
    /// <param name="databaseNames">Full database names by id, covering every database of the account.</param>
    /// <param name="isDedicated">Whether a database of the account names this user as its dedicated one.</param>
    /// <returns>The wire shape.</returns>
    public static DatabaseUserDetailDto Detail(
        DatabaseUser user,
        IReadOnlyDictionary<Guid, string> ownerNames,
        IReadOnlyDictionary<Guid, string> databaseNames,
        bool isDedicated)
    {
        var row = From(user, ownerNames, databaseNames, isDedicated);
        var held = user.Grants.Select(grant => { return grant.DatabaseId; }).ToHashSet();
        var grantable = databaseNames
            .Where(database => { return !held.Contains(database.Key); })
            .OrderBy(database => { return database.Value; }, StringComparer.Ordinal)
            .Select(database => { return new GrantableDatabaseDto(database.Key, database.Value); })
            .ToList();

        return new DatabaseUserDetailDto(
            row.Id,
            row.AccountId,
            row.OwnerName,
            row.NameSuffix,
            row.FullName,
            row.IsDedicated,
            row.Grants,
            row.CreatedAt,
            grantable);
    }

    /// <summary>A user as created, carrying the password minted for it.</summary>
    /// <param name="user">The user.</param>
    /// <param name="password">The generated password.</param>
    /// <returns>The wire shape.</returns>
    public static CreatedDatabaseUserDto Created(DatabaseUser user, SensitiveString password)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(password);

        return new CreatedDatabaseUserDto(user.Id, user.AccountId, user.NameSuffix, user.FullName, password, user.CreatedAt);
    }

    /// <summary>The grants of a user, ordered by database name.</summary>
    /// <param name="user">The user, loaded with its grants.</param>
    /// <param name="databaseNames">Full database names by id.</param>
    /// <returns>One entry per grant whose database is named; a grant on a database the caller cannot see is left out.</returns>
    private static List<DatabaseUserGrantDto> Grants(DatabaseUser user, IReadOnlyDictionary<Guid, string> databaseNames)
    {
        return user.Grants
            .Where(grant => { return databaseNames.ContainsKey(grant.DatabaseId); })
            .OrderBy(grant => { return databaseNames[grant.DatabaseId]; }, StringComparer.Ordinal)
            .Select(grant =>
            {
                return new DatabaseUserGrantDto(
                    grant.DatabaseId,
                    databaseNames[grant.DatabaseId],
                    DatabasePrivilegeSets.MembersOf(grant.Privileges));
            })
            .ToList();
    }
}
