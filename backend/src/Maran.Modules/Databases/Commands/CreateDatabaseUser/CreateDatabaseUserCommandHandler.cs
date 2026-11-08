using Maran.Agent.Client.Interfaces;
using Maran.Modules.Databases.Common;
using Maran.Modules.Databases.Domain.Entities;
using Maran.Modules.Databases.Domain.Policies;
using Maran.Modules.Databases.Interfaces;
using Maran.Modules.Databases.Mappers;
using Maran.Modules.Databases.Persistence;
using Maran.Modules.Databases.Resources;
using Maran.Modules.Databases.Services;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Maran.SharedKernel.Security;
using Microsoft.Extensions.Logging;

namespace Maran.Modules.Databases.Commands.CreateDatabaseUser;

/// <summary>
/// Handles <see cref="CreateDatabaseUserCommand"/>: refuses what the account's own rows do not allow,
/// creates the login through the agent, and only then records the row that owns it, compensating on the
/// server if the row cannot be written.
/// </summary>
/// <remarks>
/// <para>
/// <b>Order: checks first, agent second, row third</b>, for the reason <see cref="CreateDatabase.CreateDatabaseCommandHandler"/>
/// gives: a login with no row is invisible and short-lived (it is dropped again below), while a row with
/// no login is a customer told they have a user whose password does not work.
/// </para>
/// <para>
/// <b>The allowance is checked twice and the second check holds.</b> The count before the agent is
/// called refuses the ordinary over-limit request without touching the server; <see cref="IDatabaseUserLimitGate"/>
/// re-takes the count under a per-account lock at the moment the row is written, so two simultaneous
/// creations cannot both take the last slot. The loser pays a create and a drop on the server, a login
/// nobody has the password of, which is why dropping it is safe.
/// </para>
/// <para>
/// The password is minted here, handed to the agent and returned once. The journal records the user's
/// name and never the value.
/// </para>
/// </remarks>
public sealed class CreateDatabaseUserCommandHandler
{
    /// <summary>MySQL's user-name ceiling, which the prefixed login name must fit inside.</summary>
    private const int MySqlUserNameMaxLength = 32;

    /// <summary>How many characters the account prefix's separator adds to a suffix.</summary>
    private const int PrefixSeparatorLength = 1;

    /// <summary>Pre-compiled log delegate for a compensation that did not take.</summary>
    private static readonly Action<ILogger, string, string, Exception?> LogCompensationFailed =
        LoggerMessage.Define<string, string>(
            LogLevel.Error,
            new EventId(1, nameof(CreateDatabaseUserCommandHandler)),
            "Created database user {UserName} could not be recorded and could not be dropped either "
            + "({AgentErrorCode}); it is now on the server with a password nobody holds and no row.");

    /// <summary>The Databases module's database context, and this module's tenant boundary.</summary>
    private readonly DatabasesDbContext _dbContext;

    /// <summary>The owning account's system user name and standing.</summary>
    private readonly IAccountDirectory _accounts;

    /// <summary>The agent, which owns everything that exists on the MySQL server.</summary>
    private readonly IAgentDbClient _agent;

    /// <summary>The atomic claim on the account's last free user slot.</summary>
    private readonly IDatabaseUserLimitGate _limitGate;

    /// <summary>This module's audit journal.</summary>
    private readonly DatabaseAuditJournal _journal;

    /// <summary>The injected time source; never the ambient clock.</summary>
    private readonly IClock _clock;

    /// <summary>Where a failed compensation is reported.</summary>
    private readonly ILogger<CreateDatabaseUserCommandHandler> _logger;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Databases module's database context.</param>
    /// <param name="accounts">The owning account's system user name.</param>
    /// <param name="agent">The agent client that creates the login.</param>
    /// <param name="limitGate">Takes the account's last free user slot atomically when the row is written.</param>
    /// <param name="journal">This module's audit journal.</param>
    /// <param name="clock">The injected time source used to stamp the new row.</param>
    /// <param name="logger">Where a failed compensation is reported.</param>
    public CreateDatabaseUserCommandHandler(
        DatabasesDbContext dbContext,
        IAccountDirectory accounts,
        IAgentDbClient agent,
        IDatabaseUserLimitGate limitGate,
        DatabaseAuditJournal journal,
        IClock clock,
        ILogger<CreateDatabaseUserCommandHandler> logger)
    {
        _dbContext = dbContext;
        _accounts = accounts;
        _agent = agent;
        _limitGate = limitGate;
        _journal = journal;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>Creates the user, refusing it before the server is touched when anything says no.</summary>
    /// <param name="command">The validated parameters; see <see cref="CreateDatabaseUserCommandValidator"/>.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    /// The new user and its password, shown once, or <c>AccountNotFound</c>, <c>DatabaseUserLimitReached</c>,
    /// <c>DatabaseUserNameTaken</c>, <c>DatabaseUserNameTooLong</c>, <c>DatabaseUserProvisioningFailed</c>,
    /// or the agent's own typed failure.
    /// </returns>
    public async Task<Result<CreatedDatabaseUserDto>> HandleAsync(
        CreateDatabaseUserCommand command,
        CancellationToken cancellationToken)
    {
        // Tenant-scoped: the directory answers null for an account this caller does not own.
        var account = await _accounts.FindAsync(command.AccountId, cancellationToken);
        if (account is null)
        {
            return await FailAsync(command, Error.Of(nameof(ErrorMessages.AccountNotFound), ErrorType.NotFound), cancellationToken);
        }

        if (account.Username.Length + PrefixSeparatorLength + command.NameSuffix.Length > MySqlUserNameMaxLength)
        {
            return await FailAsync(command, Error.Of(nameof(ErrorMessages.DatabaseUserNameTooLong), ErrorType.Validation), cancellationToken);
        }

        // Asked of the account's own rows, through the tenant filter, never of the server's names. The
        // predicate repeats the account for the reason the database creation handler gives: it is
        // redundant only while the filter and the prefix make a cross-tenant clash impossible.
        var nameTaken = await _dbContext.DatabaseUsers
            .AsNoTracking()
            .AnyAsync(
                user => user.AccountId == command.AccountId && user.NameSuffix == command.NameSuffix,
                cancellationToken);
        if (nameTaken)
        {
            return await FailAsync(command, Error.Of(nameof(ErrorMessages.DatabaseUserNameTaken), ErrorType.Conflict), cancellationToken);
        }

        var extra = await _dbContext.DatabaseUsers
            .AsNoTracking()
            .CountAsync(
                user => user.AccountId == command.AccountId
                    && !_dbContext.Databases.Any(database =>
                        database.AccountId == user.AccountId && database.DbUserNameSuffix == user.NameSuffix),
                cancellationToken);
        if (extra >= DatabaseUserLimits.MaximumExtraUsersPerAccount)
        {
            return await FailAsync(command, Error.Of(nameof(ErrorMessages.DatabaseUserLimitReached), ErrorType.Conflict), cancellationToken);
        }

        var password = ProvisionedPasswordGenerator.Generate();

        var provisioned = await _agent.CreateUserAsync(account.Username, command.NameSuffix, password, cancellationToken);
        if (!provisioned.IsSuccess)
        {
            return await FailAsync(command, provisioned.Error!, cancellationToken);
        }

        // The full name is the agent's answer, not rebuilt from the suffix, for the reason the database row gives.
        var user = new DatabaseUser(
            Guid.NewGuid(), command.AccountId, command.NameSuffix, provisioned.Value, _clock.UtcNow);

        var recorded = await RecordAsync(user, account.Username, command, cancellationToken);
        if (!recorded.IsSuccess)
        {
            return Result<CreatedDatabaseUserDto>.Fail(recorded.Error!);
        }

        await _journal.RecordSuccessAsync(
            DatabaseAuditActions.DatabaseUserCreated, user.FullName, command.IpAddress, command.UserAgent, cancellationToken);

        return Result<CreatedDatabaseUserDto>.Ok(DatabaseUserMapper.Created(user, password));
    }

    /// <summary>Writes the row for a login the agent has already made, compensating if it cannot.</summary>
    /// <param name="user">The row to write.</param>
    /// <param name="accountUsername">The owning account's system user name, which addresses the compensating drop.</param>
    /// <param name="command">The creation being recorded.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>Success, or the typed failure the customer is answered with.</returns>
    private async Task<Result> RecordAsync(
        DatabaseUser user,
        string accountUsername,
        CreateDatabaseUserCommand command,
        CancellationToken cancellationToken)
    {
        try
        {
            if (await _limitGate.TryTakeAsync(user, DatabaseUserLimits.MaximumExtraUsersPerAccount, cancellationToken))
            {
                return Result.Ok();
            }

            // Another creation took the last slot while this one was on the server. Nobody has this
            // login's password, so it goes, and the refusal is the same sentence the ordinary one gives.
            await CompensateAsync(accountUsername, command, cancellationToken);

            return Result.Fail(await FailedErrorAsync(
                command, Error.Of(nameof(ErrorMessages.DatabaseUserLimitReached), ErrorType.Conflict), cancellationToken));
        }
        catch (DbUpdateException)
        {
            _dbContext.DatabaseUsers.Remove(user);

            // Whatever the database refused, no row owns the login this request made: it is an orphan.
            await CompensateAsync(accountUsername, command, cancellationToken);

            return Result.Fail(await FailedErrorAsync(
                command, Error.Of(nameof(ErrorMessages.DatabaseUserProvisioningFailed), ErrorType.Failure), cancellationToken));
        }
    }

    /// <summary>Drops a login the agent made but no row owns.</summary>
    /// <param name="accountUsername">The owning account's system user name.</param>
    /// <param name="command">The creation being undone; its suffix addresses the drop.</param>
    /// <param name="cancellationToken">Cancels the drop.</param>
    /// <remarks>Best effort and logged: the customer is already being told the creation failed.</remarks>
    private async Task CompensateAsync(
        string accountUsername,
        CreateDatabaseUserCommand command,
        CancellationToken cancellationToken)
    {
        var dropped = await _agent.DropUserAsync(accountUsername, command.NameSuffix, cancellationToken);
        if (!dropped.IsSuccess)
        {
            LogCompensationFailed(_logger, command.NameSuffix, dropped.Error!.Code, null);
        }
    }

    /// <summary>Journals a refused creation and hands back the code to answer with.</summary>
    /// <param name="command">The creation that was refused.</param>
    /// <param name="error">The typed failure to answer with.</param>
    /// <param name="cancellationToken">Cancels the journal write.</param>
    /// <returns><paramref name="error"/>, unchanged.</returns>
    private async Task<Error> FailedErrorAsync(
        CreateDatabaseUserCommand command,
        Error error,
        CancellationToken cancellationToken)
    {
        await _journal.RecordFailureAsync(
            DatabaseAuditActions.DatabaseUserCreated, command.NameSuffix, command.IpAddress, command.UserAgent, cancellationToken);

        return error;
    }

    /// <summary>Journals a refused creation and returns it as the typed failure.</summary>
    /// <param name="command">The creation that was refused.</param>
    /// <param name="error">The typed failure to answer with.</param>
    /// <param name="cancellationToken">Cancels the journal write.</param>
    /// <returns>The failed result carrying <paramref name="error"/>.</returns>
    private async Task<Result<CreatedDatabaseUserDto>> FailAsync(
        CreateDatabaseUserCommand command,
        Error error,
        CancellationToken cancellationToken)
    {
        return Result<CreatedDatabaseUserDto>.Fail(await FailedErrorAsync(command, error, cancellationToken));
    }
}
