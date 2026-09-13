namespace Maran.Modules.Databases.Domain.Entities;

/// <summary>
/// One MySQL database created for an account, together with the dedicated user it was created with
/// (spec §11). This row — not the names on the server — is the record of who owns what.
/// </summary>
/// <remarks>
/// <para>
/// <b>This entity is the tenant boundary of the whole module.</b> MySQL's database namespace is
/// global to the server and the server has no notion of a tenant, so a name only looks like it
/// belongs to an account because of the prefix the panel put there. Deciding ownership from those
/// names is a prefix scan, and a prefix scan aliases account <c>alice</c> onto <c>alice_bob</c>'s
/// databases — <c>alice_bob_shop</c> starts with <c>alice_</c>. Every listing, read, drop and
/// password reset in this module is therefore authorised by these rows through the context's tenant
/// query filter, exactly as a site is, and the agent's own <c>ListDatabases</c> is used for nothing
/// but operator diagnostics.
/// </para>
/// <para>
/// <b>There is no password column, of any kind — not plaintext, not encrypted, not a hash.</b> The
/// panel mints a password, shows it once and forgets it; the server's own hash is the only copy in
/// the system. A stored copy, however it is encrypted, is a copy that can be read out of the panel's
/// database, and a panel that can read back every customer's database password is a single theft
/// away from every customer's data. A customer who lost theirs gets a new one set
/// (<c>ResetDatabasePassword</c>), never the old one shown again.
/// </para>
/// <para>
/// Nothing here has a public setter and there is no method that changes any of it, because nothing
/// about a database changes after it is made: renaming a MySQL database is a dump and reload, and
/// the password — the one thing that does change — is deliberately not here.
/// </para>
/// </remarks>
public sealed class Database
{
    /// <summary>The row's identity, and the only identifier a customer's request may name.</summary>
    public Guid Id { get; private set; }

    /// <summary>The account that owns this database. Every tenant-scoped query is closed over this column.</summary>
    public Guid AccountId { get; private set; }

    /// <summary>The name the customer asked for, without the account prefix.</summary>
    /// <remarks>
    /// What the customer typed and what a screen shows them, so a customer never has to know the
    /// prefix exists. Unique within the account and NOT across the server: the prefix is what makes
    /// <c>shop</c> available to every account at once.
    /// </remarks>
    public string Name { get; private set; }

    /// <summary>The fully-qualified name MySQL actually holds, as the agent reported it.</summary>
    /// <remarks>
    /// Recorded rather than rebuilt from <see cref="Name"/> and the account's user name. Rebuilding
    /// would make this row's truth depend on the panel and the agent agreeing about a separator
    /// forever, and the day they disagreed the panel would address the wrong database — or none —
    /// with a drop.
    /// </remarks>
    public string FullName { get; private set; }

    /// <summary>The fully-qualified name of the dedicated MySQL user, as the agent reported it.</summary>
    /// <remarks>
    /// Recorded because the server cannot answer it: MySQL records which users are GRANTED on a
    /// database, not which one was "its" user, and the customer names the two halves independently.
    /// The pairing exists here and nowhere else, which is why a drop that guessed it would either
    /// strand a live credential or remove one belonging to another of the account's databases.
    /// </remarks>
    public string DbUserName { get; private set; }

    /// <summary>The suffix of <see cref="DbUserName"/>, as the customer asked for it.</summary>
    /// <remarks>
    /// Kept beside the full name because every agent call takes the SUFFIX — the agent applies the
    /// prefix itself, so that a request cannot express another tenant's user rather than merely
    /// being refused one. Without this column a drop or a reset would have to strip a prefix off
    /// <see cref="DbUserName"/> to say what it means, which is the string surgery this row exists
    /// to avoid.
    /// </remarks>
    public string DbUserNameSuffix { get; private set; }

    /// <summary>The instant the database was created.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Records a database the agent has already created on the server.</summary>
    /// <param name="id">The row's identity.</param>
    /// <param name="accountId">The account that owns this database.</param>
    /// <param name="name">The name the customer asked for, without the account prefix.</param>
    /// <param name="fullName">The fully-qualified name MySQL holds, as the agent reported it.</param>
    /// <param name="dbUserName">The fully-qualified dedicated user name, as the agent reported it.</param>
    /// <param name="dbUserNameSuffix">The user-name suffix the customer asked for.</param>
    /// <param name="createdAt">The creation instant, taken from <see cref="IClock"/>.</param>
    public Database(
        Guid id,
        Guid accountId,
        string name,
        string fullName,
        string dbUserName,
        string dbUserNameSuffix,
        DateTimeOffset createdAt)
    {
        Id = id;
        AccountId = accountId;
        Name = name;
        FullName = fullName;
        DbUserName = dbUserName;
        DbUserNameSuffix = dbUserNameSuffix;
        CreatedAt = createdAt;
    }

    /// <summary>Parameterless constructor required by EF Core materialization.</summary>
    private Database()
    {
        Name = string.Empty;
        FullName = string.Empty;
        DbUserName = string.Empty;
        DbUserNameSuffix = string.Empty;
    }
}
