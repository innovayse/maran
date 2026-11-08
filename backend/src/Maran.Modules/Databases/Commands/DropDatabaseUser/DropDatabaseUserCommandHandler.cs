using Maran.Agent.Client.Interfaces;
using Maran.Modules.Databases.Persistence;
using Maran.Modules.Databases.Resources;
using Maran.Modules.Databases.Services;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Databases.Commands.DropDatabaseUser;

/// <summary>
/// Handles <see cref="DropDatabaseUserCommand"/>: refuses a database's dedicated user, drops the login
/// on the server, and then the row that owned it.
/// </summary>
/// <remarks>
/// <para>
/// <b>A dedicated user is never dropped alone.</b> It is the user a database was created with, found by
/// asking the account's database rows whether one names it, and it is dropped with that database. The
/// refusal is a conflict, not a not-found: the user exists and is the caller's, and the sentence says
/// what to do instead.
/// </para>
/// <para>
/// The agent runs first, as everywhere in this module: a login removed with its row still present is
/// visible and retryable, while a row removed with the login still there is a credential nobody in the
/// panel can see. The user's grants go by the cascade from the user row.
/// </para>
/// </remarks>
public sealed class DropDatabaseUserCommandHandler
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
    /// <param name="agent">The agent client that drops the login.</param>
    /// <param name="journal">This module's audit journal.</param>
    public DropDatabaseUserCommandHandler(
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

    /// <summary>Drops the user.</summary>
    /// <param name="command">Which user to drop.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    /// Success, or <c>DatabaseUserNotFound</c>, <c>DatabaseUserIsDedicated</c>, <c>AccountNotFound</c>,
    /// or the agent's own typed failure.
    /// </returns>
    public async Task<Result> HandleAsync(DropDatabaseUserCommand command, CancellationToken cancellationToken)
    {
        var user = await _dbContext.DatabaseUsers
            .SingleOrDefaultAsync(row => row.Id == command.UserId, cancellationToken);
        if (user is null)
        {
            // The subject is the identifier the caller supplied, because no name is known.
            return await FailAsync(
                command,
                command.UserId.ToString(),
                Error.Of(nameof(ErrorMessages.DatabaseUserNotFound), ErrorType.NotFound),
                cancellationToken);
        }

        var dedicated = await _dbContext.Databases
            .AsNoTracking()
            .AnyAsync(
                database => database.AccountId == user.AccountId && database.DbUserNameSuffix == user.NameSuffix,
                cancellationToken);
        if (dedicated)
        {
            return await FailAsync(
                command,
                user.FullName,
                Error.Of(nameof(ErrorMessages.DatabaseUserIsDedicated), ErrorType.Conflict),
                cancellationToken);
        }

        var account = await _accounts.FindAsync(user.AccountId, cancellationToken);
        if (account is null)
        {
            return await FailAsync(
                command, user.FullName, Error.Of(nameof(ErrorMessages.AccountNotFound), ErrorType.NotFound), cancellationToken);
        }

        var dropped = await _agent.DropUserAsync(account.Username, user.NameSuffix, cancellationToken);
        if (!dropped.IsSuccess)
        {
            return await FailAsync(command, user.FullName, dropped.Error!, cancellationToken);
        }

        // Captured before the row goes: the full name is the only thing about a dropped user anybody
        // will later be able to search for.
        var fullName = user.FullName;

        _dbContext.DatabaseUsers.Remove(user);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _journal.RecordSuccessAsync(
            DatabaseAuditActions.DatabaseUserDropped, fullName, command.IpAddress, command.UserAgent, cancellationToken);

        return Result.Ok();
    }

    /// <summary>Journals a refused drop and returns it as the typed failure.</summary>
    /// <param name="command">The drop that was refused.</param>
    /// <param name="subject">The user's full name, or the supplied identifier when no row was found.</param>
    /// <param name="error">The typed failure to answer with.</param>
    /// <param name="cancellationToken">Cancels the journal write.</param>
    /// <returns>The failed result carrying <paramref name="error"/>.</returns>
    private async Task<Result> FailAsync(
        DropDatabaseUserCommand command,
        string subject,
        Error error,
        CancellationToken cancellationToken)
    {
        await _journal.RecordFailureAsync(
            DatabaseAuditActions.DatabaseUserDropped, subject, command.IpAddress, command.UserAgent, cancellationToken);

        return Result.Fail(error);
    }
}
