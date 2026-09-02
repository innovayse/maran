using Maran.Agent.Client.Interfaces;
using Maran.Modules.Databases.Common;
using Maran.Modules.Databases.Persistence;
using Maran.Modules.Databases.Resources;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Databases.Commands.DropDatabase;

/// <summary>
/// Handles <see cref="DropDatabaseCommand"/>: removes the database and its dedicated user from the
/// server, and then the row that owned them.
/// </summary>
/// <remarks>
/// <para>
/// <b>Which database is dropped is decided by the panel's row and by nothing else.</b> The row is
/// loaded through the tenant-filtered context, so another customer's identifier finds nothing and
/// answers 404 — never 403, which would confirm the database exists. The agent is then addressed by
/// the SUFFIXES this row recorded, so the drop cannot reach past the account even if the row were
/// wrong: the agent applies the account prefix itself.
/// </para>
/// <para>
/// The dedicated user's suffix comes from the row rather than from the database's name. The customer
/// named the two halves independently, so a drop that derived one from the other would either leave
/// a live credential on the server or remove one belonging to another of the account's databases.
/// </para>
/// <para>
/// The agent runs first, as everywhere in this module. A database removed with the row still present
/// is visible, retryable and converges — the agent reports a second drop as <c>NotFound</c>. A row
/// removed with the database still there is a customer's data nobody in the panel can see and nobody
/// can now remove, still counted by nothing and still occupying the server.
/// </para>
/// </remarks>
public sealed class DropDatabaseCommandHandler
{
    /// <summary>The Databases module's database context, and this module's tenant boundary.</summary>
    private readonly DatabasesDbContext _dbContext;

    /// <summary>The owning account's system user name, which addresses every agent operation.</summary>
    private readonly IAccountDirectory _accounts;

    /// <summary>The agent, which owns everything that exists on the MySQL server.</summary>
    private readonly IAgentDbClient _agent;

    /// <summary>This module's audit journal.</summary>
    private readonly DatabaseAuditJournal _journal;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Databases module's database context.</param>
    /// <param name="accounts">The owning account's system user name.</param>
    /// <param name="agent">The agent client that drops the database.</param>
    /// <param name="journal">This module's audit journal.</param>
    public DropDatabaseCommandHandler(
        DatabasesDbContext dbContext,
        IAccountDirectory accounts,
        IAgentDbClient agent,
        DatabaseAuditJournal journal)
    {
        _dbContext = dbContext;
        _accounts = accounts;
        _agent = agent;
        _journal = journal;
    }

    /// <summary>Drops the database. Idempotent from the customer's side: a second attempt is not found.</summary>
    /// <param name="command">Which database to drop.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Success, or <c>DatabaseNotFound</c>, <c>AccountNotFound</c>, or the agent's own typed failure.</returns>
    public async Task<Result<bool>> HandleAsync(DropDatabaseCommand command, CancellationToken cancellationToken)
    {
        var database = await _dbContext.Databases
            .SingleOrDefaultAsync(row => row.Id == command.DatabaseId, cancellationToken);
        if (database is null)
        {
            // The subject is the identifier the caller supplied, because no name is known — a probe
            // for a database the caller may not see still leaves a trace naming what was probed for.
            return await FailAsync(
                command,
                command.DatabaseId.ToString(),
                nameof(ErrorMessages.DatabaseNotFound),
                cancellationToken);
        }

        var account = await _accounts.FindAsync(database.AccountId, cancellationToken);
        if (account is null)
        {
            return await FailAsync(
                command, database.Name, nameof(ErrorMessages.AccountNotFound), cancellationToken);
        }

        var dropped = await _agent.DropAsync(
            account.Username, database.Name, database.DbUserNameSuffix, cancellationToken);
        if (!dropped.IsSuccess)
        {
            return await FailAsync(command, database.Name, dropped.Error!.Code, cancellationToken);
        }

        // Captured before the row goes: the journal records the name, which is the only thing about
        // a dropped database anybody will later be able to search for.
        var name = database.Name;

        _dbContext.Databases.Remove(database);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _journal.RecordSuccessAsync(
            AuditActions.DatabaseDropped, name, command.IpAddress, command.UserAgent, cancellationToken);

        return Result<bool>.Ok(true);
    }

    /// <summary>Journals a refused drop and returns it as the typed failure.</summary>
    /// <param name="command">The drop that was refused.</param>
    /// <param name="subject">The database's name, or the supplied identifier when no row was found.</param>
    /// <param name="code">The machine-stable code to answer with.</param>
    /// <param name="cancellationToken">Cancels the journal write.</param>
    /// <returns>The failed result carrying <paramref name="code"/>.</returns>
    private async Task<Result<bool>> FailAsync(
        DropDatabaseCommand command,
        string subject,
        string code,
        CancellationToken cancellationToken)
    {
        await _journal.RecordFailureAsync(
            AuditActions.DatabaseDropped, subject, command.IpAddress, command.UserAgent, cancellationToken);

        return Result<bool>.Fail(Error.Of(code));
    }
}
