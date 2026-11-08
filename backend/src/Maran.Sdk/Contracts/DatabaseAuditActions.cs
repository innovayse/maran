namespace Maran.Sdk.Contracts;

/// <summary>
/// The machine-stable audit action names for database, database-user and grant-repair actions. Constants rather than an enum: a marketplace
/// module records actions this assembly was never compiled knowing about, and an enum could not be
/// extended from outside.
/// </summary>
/// <remarks>
/// One of the Sdk's <c>*AuditActions</c> classes, which together are the closed set of actions the
/// shipped modules record; each is named for the area whose journal writes its constants.
/// </remarks>
public static class DatabaseAuditActions
{
    /// <summary>A database and its dedicated user were created on the MySQL server.</summary>
    public const string DatabaseCreated = "DatabaseCreated";

    /// <summary>A database and its dedicated user were dropped, and the customer's data with them.</summary>
    public const string DatabaseDropped = "DatabaseDropped";

    /// <summary>
    /// A database user was given a new password. Recorded because it is the only recovery for a
    /// credential nobody keeps a copy of, so a reset somebody else performed is exactly the event a
    /// customer whose application stopped connecting needs explained. The entry names the database,
    /// never the value.
    /// </summary>
    public const string DatabasePasswordReset = "DatabasePasswordReset";

    /// <summary>
    /// An extra database user was created for an account. The entry names the user, never the password
    /// that was minted for it.
    /// </summary>
    public const string DatabaseUserCreated = "DatabaseUserCreated";

    /// <summary>
    /// An extra database user was dropped, and every privilege it held with it. A database's dedicated
    /// user is dropped with its database and is recorded as <see cref="DatabaseDropped"/>, not here.
    /// </summary>
    public const string DatabaseUserDropped = "DatabaseUserDropped";

    /// <summary>
    /// A database user was given a new password through the users screen. The entry names the user,
    /// never the value, for the reason <see cref="DatabasePasswordReset"/> gives.
    /// </summary>
    public const string DatabaseUserPasswordReset = "DatabaseUserPasswordReset";

    /// <summary>
    /// The set of privileges a database user holds on one database was replaced. Recorded because it
    /// is the change that widens or narrows what a credential can read and write, so a customer whose
    /// application started failing, or an operator asking who widened a login, finds it here. The
    /// subject names the user and the database; the privileges themselves are not copied into the
    /// journal, which is never deleted.
    /// </summary>
    public const string DatabasePrivilegesChanged = "DatabasePrivilegesChanged";

    /// <summary>
    /// The database server's grant table was rewritten so that every pattern this panel had issued
    /// names one database again. Recorded on the refusals as well as on the repairs, and the reason
    /// each is worth a line differs.
    /// <para>
    /// A success is the panel's only record of WHO narrowed live access for every customer on the
    /// host at once. The agent logs each row it touched, but the host's log does not know which
    /// operator asked, and an operation of this reach with no attributable entry is one nobody can
    /// account for afterwards.
    /// </para>
    /// <para>
    /// A refusal is worth a line because of what it means: the figure the operator confirmed no
    /// longer described the server. That is either an ordinary race — another administrator ran the
    /// repair first — or someone attempting the write without having read a report, and the journal
    /// is where the second becomes visible as a pattern.
    /// </para>
    /// <para>
    /// The subject is a count, never a name. Every row this operation refuses is refused precisely
    /// because the panel did not write it, so its <c>Db</c> and <c>User</c> columns are another
    /// tenant's identifiers or an operator's own credential — and this journal is never deleted
    /// (rules/security.md).
    /// </para>
    /// </summary>
    public const string DatabaseGrantsRepaired = "DatabaseGrantsRepaired";
}
