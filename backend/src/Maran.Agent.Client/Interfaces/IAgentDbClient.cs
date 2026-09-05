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
    Task<Result<bool>> DropAsync(
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
    Task<Result<bool>> SetPasswordAsync(
        string accountUsername,
        string dbUsername,
        SensitiveString password,
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
}
