using Maran.Modules.Databases.Domain.Enums;

namespace Maran.Modules.Databases.Domain.Entities;

/// <summary>
/// One MySQL login owned by an account, with the privileges it holds on the account's databases. A
/// database's dedicated user is one of these, and so is every additional login the customer creates.
/// </summary>
/// <remarks>
/// <para>
/// <b>Dedicated is derived, never stored.</b> A user is a database's dedicated one when that database's
/// <see cref="Database.DbUserNameSuffix"/> equals this user's <see cref="NameSuffix"/>, and a second
/// flag saying so would be a second place that could disagree with the first. The rule that follows
/// from it — a dedicated user is dropped with its database and never alone — is enforced by the drop
/// handlers, which ask the database rows.
/// </para>
/// <para>
/// <b>There is no password column, of any kind</b>, for the reason <see cref="Database"/> gives: the
/// panel mints a password, shows it once and forgets it, and the server's own hash is the only copy.
/// </para>
/// <para>
/// This entity is the aggregate for its grants. A handler never constructs a <see cref="DatabaseGrant"/>
/// or assigns one's privileges; it calls <see cref="ReplacePrivileges"/>, which holds the rule that a
/// grant is one row per database and that an empty set means no row.
/// </para>
/// </remarks>
public sealed class DatabaseUser
{
    /// <summary>The grants this user holds, one per database at most.</summary>
    private readonly List<DatabaseGrant> _grants = [];

    /// <summary>The row's identity, and the only identifier a customer's request may name.</summary>
    public Guid Id { get; private set; }

    /// <summary>The account that owns this user. Every tenant-scoped query is closed over this column.</summary>
    public Guid AccountId { get; private set; }

    /// <summary>The name the customer asked for, without the account prefix.</summary>
    /// <remarks>
    /// Kept beside the full name because every agent call takes the SUFFIX: the agent applies the prefix
    /// itself, so a request cannot express another tenant's user rather than merely being refused one.
    /// </remarks>
    public string NameSuffix { get; private set; }

    /// <summary>The fully-qualified login name MySQL holds, as the agent reported it.</summary>
    public string FullName { get; private set; }

    /// <summary>The instant the user was created.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>What this user holds on the account's databases. Loaded only when a query includes it.</summary>
    public IReadOnlyCollection<DatabaseGrant> Grants
    {
        get
        {
            return _grants;
        }
    }

    /// <summary>Records a login the agent has already created on the server.</summary>
    /// <param name="id">The row's identity.</param>
    /// <param name="accountId">The account that owns the user.</param>
    /// <param name="nameSuffix">The login name the customer asked for, without the account prefix.</param>
    /// <param name="fullName">The fully-qualified login name, as the agent reported it.</param>
    /// <param name="createdAt">The creation instant, taken from <see cref="IClock"/>.</param>
    public DatabaseUser(Guid id, Guid accountId, string nameSuffix, string fullName, DateTimeOffset createdAt)
    {
        Id = id;
        AccountId = accountId;
        NameSuffix = nameSuffix;
        FullName = fullName;
        CreatedAt = createdAt;
    }

    /// <summary>Parameterless constructor required by EF Core materialization.</summary>
    private DatabaseUser()
    {
        NameSuffix = string.Empty;
        FullName = string.Empty;
    }

    /// <summary>
    /// Makes the privileges this user holds on one database exactly <paramref name="privileges"/>: adds
    /// the grant if there was none, changes it if there was one, and removes it when the set is empty.
    /// </summary>
    /// <param name="databaseId">The database the privileges are held on.</param>
    /// <param name="privileges">The complete set to hold; <see cref="DatabasePrivilegeSet.None"/> removes the grant.</param>
    /// <param name="grantId">The identity a newly created grant takes; ignored when the grant exists or the set is empty.</param>
    /// <param name="at">The instant, taken from <see cref="IClock"/>, a newly created grant records.</param>
    /// <remarks>
    /// Callable only on a user loaded with <see cref="Grants"/>: on one loaded without them it would
    /// add a second grant for a database that already has one, which the unique index then refuses.
    /// </remarks>
    public void ReplacePrivileges(Guid databaseId, DatabasePrivilegeSet privileges, Guid grantId, DateTimeOffset at)
    {
        var existing = _grants.SingleOrDefault(grant =>
        {
            return grant.DatabaseId == databaseId;
        });

        if (privileges == DatabasePrivilegeSet.None)
        {
            if (existing is not null)
            {
                _grants.Remove(existing);
            }

            return;
        }

        if (existing is null)
        {
            _grants.Add(new DatabaseGrant(grantId, Id, databaseId, privileges, at));

            return;
        }

        existing.ReplacePrivileges(privileges);
    }
}
