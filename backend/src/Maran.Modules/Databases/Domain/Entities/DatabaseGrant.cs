using Maran.Modules.Databases.Domain.Enums;

namespace Maran.Modules.Databases.Domain.Entities;

/// <summary>
/// The privileges one <see cref="DatabaseUser"/> holds on one <see cref="Database"/>. A user holds at
/// most one row per database, and holding nothing on a database is having no row for it.
/// </summary>
/// <remarks>
/// <para>
/// Created and changed only through <see cref="DatabaseUser"/>, which is the aggregate: the rule that a
/// row never holds <see cref="DatabasePrivilegeSet.None"/> is the user's, so a handler cannot write one
/// by reaching for the row.
/// </para>
/// <para>
/// This row carries no account id of its own and is a customer's row all the same: it is scoped through
/// its required user, exactly as a site's hostname is scoped through its site, so the module's query
/// filter reads <c>grant.DatabaseUser.AccountId</c> and a grant never has to be remembered in a handler.
/// </para>
/// </remarks>
public sealed class DatabaseGrant
{
    /// <summary>The row's identity.</summary>
    public Guid Id { get; private set; }

    /// <summary>The user that holds the privileges.</summary>
    public Guid DatabaseUserId { get; private set; }

    /// <summary>The user the privileges belong to; the route a query filter reaches the account through.</summary>
    public DatabaseUser DatabaseUser { get; private set; } = null!;

    /// <summary>The database the privileges are held on.</summary>
    public Guid DatabaseId { get; private set; }

    /// <summary>The privileges held. Never <see cref="DatabasePrivilegeSet.None"/>.</summary>
    public DatabasePrivilegeSet Privileges { get; private set; }

    /// <summary>The instant the user was first granted anything on the database.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Records a grant the agent has already applied on the server.</summary>
    /// <param name="id">The row's identity.</param>
    /// <param name="databaseUserId">The user that holds the privileges.</param>
    /// <param name="databaseId">The database the privileges are held on.</param>
    /// <param name="privileges">The privileges held; must not be empty.</param>
    /// <param name="createdAt">The grant instant, taken from <see cref="IClock"/>.</param>
    internal DatabaseGrant(
        Guid id,
        Guid databaseUserId,
        Guid databaseId,
        DatabasePrivilegeSet privileges,
        DateTimeOffset createdAt)
    {
        Id = id;
        DatabaseUserId = databaseUserId;
        DatabaseId = databaseId;
        Privileges = privileges;
        CreatedAt = createdAt;
    }

    /// <summary>Parameterless constructor required by EF Core materialization.</summary>
    private DatabaseGrant()
    {
    }

    /// <summary>Replaces the privileges held.</summary>
    /// <param name="privileges">The new set; must not be empty.</param>
    internal void ReplacePrivileges(DatabasePrivilegeSet privileges)
    {
        Privileges = privileges;
    }
}
