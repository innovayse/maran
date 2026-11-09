using Maran.Agent.Client.Services.DbService;
using Maran.SharedKernel.Results;
using Maran.SharedKernel.Security;

namespace Maran.Agent.Client.Interfaces;

/// <summary>
/// The panel's view of the agent's database operations. Deciding which databases an account owns is
/// the panel's job; this is only the creation, removal, enumeration and measurement of them on the
/// server.
/// </summary>
/// <remarks>
/// Every name here is a SUFFIX, never a fully-qualified one. The agent applies the account prefix
/// itself, so a request cannot express "another tenant's database" rather than merely being refused
/// one — the server has no notion of a tenant, and there is nothing on the far side that could
/// authorise the question.
///
/// No method returns a password and none may be added. The panel mints one, shows it once and
/// forgets it; the server's own hash is the only copy in the system. A customer who lost theirs gets
/// a new one set through <see cref="CreateAsync"/>'s counterpart on the SFTP side or a fresh
/// database credential, never the old value shown again.
/// </remarks>
public interface IAgentDbClient
{
    /// <summary>Creates a database and a dedicated user granted full privileges on that database only.</summary>
    /// <param name="accountUsername">System username of the owning account; the agent namespaces both names under it.</param>
    /// <param name="databaseName">Database name suffix chosen by the customer.</param>
    /// <param name="dbUsername">Username suffix for the dedicated database user, chosen independently of the database name.</param>
    /// <param name="password">
    /// The password the panel just minted for the new user. Carried in a non-printing wrapper so
    /// that no log line, interpolation or exception message can render it, and stripped from the
    /// agent's own error text before that text is logged.
    /// </param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>
    /// The fully-qualified names as created, or a typed failure — <c>AgentAlreadyExists</c> when the
    /// pair is already there, in which case the existing password is deliberately NOT changed.
    /// </returns>
    Task<Result<CreatedDatabaseDto>> CreateAsync(
        string accountUsername,
        string databaseName,
        string dbUsername,
        SensitiveString password,
        CancellationToken cancellationToken);

    /// <summary>Drops a database and the dedicated user created alongside it.</summary>
    /// <param name="accountUsername">System username of the owning account.</param>
    /// <param name="databaseName">Database name suffix, as passed to <see cref="CreateAsync"/>.</param>
    /// <param name="dbUsername">
    /// Username suffix of the dedicated user, dropped with the database. Required rather than
    /// derived: the customer names the two halves independently, so a drop that guessed would either
    /// leave a live credential behind or remove somebody else's.
    /// </param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>Success, or a typed failure — <c>AgentNotFound</c> when there was no such database.</returns>
    Task<Result> DropAsync(
        string accountUsername,
        string databaseName,
        string dbUsername,
        CancellationToken cancellationToken);

    /// <summary>Sets an existing database user's password, and nothing else.</summary>
    /// <param name="accountUsername">System username of the owning account; the agent namespaces the user name under it.</param>
    /// <param name="dbUsername">Username suffix of the dedicated database user, as passed to <see cref="CreateAsync"/>.</param>
    /// <param name="password">
    /// The replacement password the panel just minted, in the same non-printing wrapper
    /// <see cref="CreateAsync"/> takes and stripped from the agent's error text before it is logged.
    /// </param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>
    /// Success, or a typed failure — <c>AgentNotFound</c> when there is no such user, which the
    /// agent answers rather than creating one.
    /// </returns>
    /// <remarks>
    /// The only way a lost password is recovered. Nothing in this system keeps a copy, and
    /// <see cref="CreateAsync"/> deliberately leaves an existing pair's password alone so that
    /// retrying a creation is safe — which leaves this call as the whole of the recovery path.
    ///
    /// It still returns no password: the caller minted the value it sent and already holds it.
    /// </remarks>
    Task<Result> SetPasswordAsync(
        string accountUsername,
        string dbUsername,
        SensitiveString password,
        CancellationToken cancellationToken);

    /// <summary>
    /// Creates an additional login for an account, granted nothing, able to connect from
    /// <c>localhost</c> only.
    /// </summary>
    /// <param name="accountUsername">System username of the owning account; the agent namespaces the login under it.</param>
    /// <param name="dbUsername">Username suffix of the new login.</param>
    /// <param name="password">
    /// The password the panel just minted for the login, in the same non-printing wrapper
    /// <see cref="CreateAsync"/> takes and stripped from the agent's error text before it is logged.
    /// </param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>
    /// The fully-qualified login name as created, or a typed failure — <c>AgentAlreadyExists</c> when
    /// the login is already on the server, in which case its password is deliberately NOT changed.
    /// </returns>
    /// <remarks>
    /// A login is given access to a database separately, by <see cref="SetPrivilegesAsync"/>. Nothing
    /// here opens a login to a network address.
    /// </remarks>
    Task<Result<string>> CreateUserAsync(
        string accountUsername,
        string dbUsername,
        SensitiveString password,
        CancellationToken cancellationToken);

    /// <summary>Drops a login and every privilege it holds.</summary>
    /// <param name="accountUsername">System username of the owning account.</param>
    /// <param name="dbUsername">Username suffix of the login to drop.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>Success, or a typed failure — <c>AgentNotFound</c> when the login is not on the server.</returns>
    /// <remarks>
    /// The agent does not know which login is a database's dedicated one and does not decide whether a
    /// login MAY be dropped on its own: that rule belongs to the panel, which holds the rows.
    /// </remarks>
    Task<Result> DropUserAsync(
        string accountUsername,
        string dbUsername,
        CancellationToken cancellationToken);

    /// <summary>Makes a login's privileges on one database exactly the set given.</summary>
    /// <param name="accountUsername">System username of the owning account.</param>
    /// <param name="dbUsername">Username suffix of the login.</param>
    /// <param name="databaseName">Database name suffix, as passed to <see cref="CreateAsync"/>.</param>
    /// <param name="privileges">
    /// The complete set the login is to hold on that database, each privilege once. An empty set
    /// revokes everything the login holds on it.
    /// </param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>
    /// Success, or a typed failure — <c>AgentNotFound</c> when the login or the database is not on the
    /// server; nothing is created.
    /// </returns>
    Task<Result> SetPrivilegesAsync(
        string accountUsername,
        string dbUsername,
        string databaseName,
        IReadOnlyCollection<DatabasePrivilegeKind> privileges,
        CancellationToken cancellationToken);

    /// <summary>Lists what the server holds under names that decode to the account.</summary>
    /// <param name="accountUsername">System username of the owning account.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>
    /// The diagnostic rows in the order the agent sent them, or a typed failure. Each row's user and
    /// size are null unless the agent established them, which it currently never does — see
    /// <see cref="DatabaseSummaryDto"/> for why absence is the honest answer there.
    /// </returns>
    Task<Result<IReadOnlyList<DatabaseSummaryDto>>> ListAsync(
        string accountUsername,
        CancellationToken cancellationToken);

    /// <summary>Reads the on-disk size of one database.</summary>
    /// <param name="accountUsername">System username of the owning account.</param>
    /// <param name="databaseName">Database name suffix, as passed to <see cref="CreateAsync"/>.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>The size in bytes, or a typed failure.</returns>
    Task<Result<ulong>> GetSizeAsync(
        string accountUsername,
        string databaseName,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reads this server's whole database-level grant table and reports what a repair would narrow,
    /// changing nothing — the pass an operator reads before anything changes.
    /// </summary>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>The census of the grant table, or the agent's own typed failure.</returns>
    /// <remarks>
    /// Every remark on <see cref="RepairGrantsAsync"/> applies here as well: it is host-wide, it
    /// returns other tenants' identifiers, and it settles nothing about exploitation.
    /// </remarks>
    Task<Result<GrantInspectionReportDto>> InspectGrantsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Reads this server's whole database-level grant table and narrows every row this panel issued
    /// whose stored name became a wildcard pattern.
    /// </summary>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>The census of the grant table after the rewrite, or the agent's own typed failure.</returns>
    /// <remarks>
    /// <para>
    /// <b>The two members here, with <see cref="InspectGrantsAsync"/>, that are not scoped to an
    /// account, and the only ones that take no name.</b> Every other call on this interface acts on a suffix under one account's prefix; this
    /// one acts on rows nobody asked about, host-wide, for every customer on the server at once. The
    /// panel therefore gates it on an administrator rather than on tenant scoping, which has nothing
    /// to scope here: see <c>DatabaseGrantsController</c>.
    /// </para>
    /// <para>
    /// <b>It returns other tenants' identifiers.</b> A refused row carries the server's raw
    /// <c>Host</c>, <c>Db</c> and <c>User</c> columns, and a row is refused precisely because this
    /// panel did not write it — so the names can be another customer's, or an operator's own
    /// hand-made credential. Nothing may hand this result to a customer.
    /// </para>
    /// <para>
    /// <b>It settles nothing about exploitation.</b> A repaired row says only that the reach is
    /// closed going forward
    /// (docs/superpowers/notes/2026-09-13-grant-repair-threat-note.md).
    /// </para>
    /// </remarks>
    Task<Result<GrantRepairReportDto>> RepairGrantsAsync(CancellationToken cancellationToken);
}
