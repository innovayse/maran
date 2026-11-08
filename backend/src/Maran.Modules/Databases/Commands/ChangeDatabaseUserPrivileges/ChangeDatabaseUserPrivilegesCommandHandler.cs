using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.DbService;
using Maran.Modules.Databases.Domain.Enums;
using Maran.Modules.Databases.Domain.Policies;
using Maran.Modules.Databases.Mappers;
using Maran.Modules.Databases.Persistence;
using Maran.Modules.Databases.Resources;
using Maran.Modules.Databases.Services;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Microsoft.Extensions.Logging;

namespace Maran.Modules.Databases.Commands.ChangeDatabaseUserPrivileges;

/// <summary>
/// Handles <see cref="ChangeDatabaseUserPrivilegesCommand"/>: sets the privileges on the server and
/// then records them, putting the server back if the record cannot be written.
/// </summary>
/// <remarks>
/// <para>
/// <b>Both rows are the caller's, and they are the same account's.</b> The user and the database are
/// each loaded through the tenant filter, so another customer's identifier answers "not found". An
/// administrator's context shows every tenant, so the handler also requires the database to belong to
/// the user's account: a grant across two accounts is a cross-tenant read and is refused as the database
/// not being found.
/// </para>
/// <para>
/// <b>The agent runs first</b> and converges the server to exactly the set (granting what is missing
/// before revoking the surplus, so an interruption leaves too much and never nothing). If the row then
/// cannot be written, the previous set is sent back, so the panel's record and the server do not
/// disagree; that restore is logged when it fails too, because only an operator can then repair it.
/// </para>
/// <para>
/// The journal names the user and the database and never the privileges: the journal is never deleted
/// and the grant row is the record of what is held.
/// </para>
/// </remarks>
public sealed class ChangeDatabaseUserPrivilegesCommandHandler
{
    /// <summary>Pre-compiled log delegate for a restore that did not take.</summary>
    private static readonly Action<ILogger, string, string, string, Exception?> LogRestoreFailed =
        LoggerMessage.Define<string, string, string>(
            LogLevel.Error,
            new EventId(1, nameof(ChangeDatabaseUserPrivilegesCommandHandler)),
            "The privileges of database user {UserName} on {DatabaseName} were changed on the server but "
            + "could not be recorded, and putting them back failed too ({AgentErrorCode}); the server and "
            + "the panel now disagree about this grant.");

    /// <summary>The Databases module's database context, and this module's tenant boundary.</summary>
    private readonly DatabasesDbContext _dbContext;

    /// <summary>The owning account's system user name.</summary>
    private readonly IAccountDirectory _accounts;

    /// <summary>The agent, which owns everything that exists on the MySQL server.</summary>
    private readonly IAgentDbClient _agent;

    /// <summary>This module's audit journal.</summary>
    private readonly DatabaseAuditJournal _journal;

    /// <summary>The injected time source; never the ambient clock.</summary>
    private readonly IClock _clock;

    /// <summary>Where a failed restore is reported.</summary>
    private readonly ILogger<ChangeDatabaseUserPrivilegesCommandHandler> _logger;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Databases module's database context.</param>
    /// <param name="accounts">The owning account's system user name.</param>
    /// <param name="agent">The agent client that sets the privileges.</param>
    /// <param name="journal">This module's audit journal.</param>
    /// <param name="clock">The injected time source used to stamp a new grant.</param>
    /// <param name="logger">Where a failed restore is reported.</param>
    public ChangeDatabaseUserPrivilegesCommandHandler(
        DatabasesDbContext dbContext,
        IAccountDirectory accounts,
        IAgentDbClient agent,
        DatabaseAuditJournal journal,
        IClock clock,
        ILogger<ChangeDatabaseUserPrivilegesCommandHandler> logger)
    {
        _dbContext = dbContext;
        _accounts = accounts;
        _agent = agent;
        _journal = journal;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>Replaces the user's privileges on the database.</summary>
    /// <param name="command">The validated change; see <see cref="ChangeDatabaseUserPrivilegesCommandValidator"/>.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    /// Success, or <c>DatabaseUserNotFound</c>, <c>DatabaseNotFound</c>, <c>AccountNotFound</c>,
    /// <c>DatabasePrivilegesNotChanged</c>, or the agent's own typed failure.
    /// </returns>
    public async Task<Result> HandleAsync(ChangeDatabaseUserPrivilegesCommand command, CancellationToken cancellationToken)
    {
        var user = await _dbContext.DatabaseUsers
            .Include(row => row.Grants)
            .SingleOrDefaultAsync(row => row.Id == command.UserId, cancellationToken);
        if (user is null)
        {
            return await FailAsync(
                command,
                command.UserId.ToString(),
                Error.Of(nameof(ErrorMessages.DatabaseUserNotFound), ErrorType.NotFound),
                cancellationToken);
        }

        var database = await _dbContext.Databases
            .AsNoTracking()
            .SingleOrDefaultAsync(
                row => row.Id == command.DatabaseId && row.AccountId == user.AccountId,
                cancellationToken);
        if (database is null)
        {
            return await FailAsync(
                command,
                $"{user.FullName}:{command.DatabaseId}",
                Error.Of(nameof(ErrorMessages.DatabaseNotFound), ErrorType.NotFound),
                cancellationToken);
        }

        var account = await _accounts.FindAsync(user.AccountId, cancellationToken);
        if (account is null)
        {
            return await FailAsync(
                command, user.FullName, Error.Of(nameof(ErrorMessages.AccountNotFound), ErrorType.NotFound), cancellationToken);
        }

        var subject = $"{user.FullName}:{database.FullName}";
        var wanted = DatabasePrivilegeSets.Of(command.Privileges);
        var previous = user.Grants
            .SingleOrDefault(grant =>
            {
                return grant.DatabaseId == database.Id;
            })?.Privileges ?? DatabasePrivilegeSet.None;

        var applied = await _agent.SetPrivilegesAsync(
            account.Username, user.NameSuffix, database.Name, KindsOf(wanted), cancellationToken);
        if (!applied.IsSuccess)
        {
            return await FailAsync(command, subject, applied.Error!, cancellationToken);
        }

        user.ReplacePrivileges(database.Id, wanted, Guid.NewGuid(), _clock.UtcNow);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // The server holds the new set and the panel's row does not. Put the server back so the two
            // agree on the old one; an unrecorded change must not stay live.
            var restored = await _agent.SetPrivilegesAsync(
                account.Username, user.NameSuffix, database.Name, KindsOf(previous), cancellationToken);
            if (!restored.IsSuccess)
            {
                LogRestoreFailed(_logger, user.FullName, database.FullName, restored.Error!.Code, null);
            }

            return await FailAsync(
                command,
                subject,
                Error.Of(nameof(ErrorMessages.DatabasePrivilegesNotChanged), ErrorType.Failure),
                cancellationToken);
        }

        await _journal.RecordSuccessAsync(
            DatabaseAuditActions.DatabasePrivilegesChanged, subject, command.IpAddress, command.UserAgent, cancellationToken);

        return Result.Ok();
    }

    /// <summary>The agent client's privilege names for a stored set.</summary>
    /// <param name="set">The set to send.</param>
    /// <returns>One entry per privilege held, in the order the screen lists them.</returns>
    private static List<DatabasePrivilegeKind> KindsOf(DatabasePrivilegeSet set)
    {
        return DatabasePrivilegeSets.MembersOf(set).Select(DatabasePrivilegeKindMapper.ToKind).ToList();
    }

    /// <summary>Journals a refused change and returns it as the typed failure.</summary>
    /// <param name="command">The change that was refused.</param>
    /// <param name="subject">The user and database names, or the supplied identifier when no row was found.</param>
    /// <param name="error">The typed failure to answer with.</param>
    /// <param name="cancellationToken">Cancels the journal write.</param>
    /// <returns>The failed result carrying <paramref name="error"/>.</returns>
    private async Task<Result> FailAsync(
        ChangeDatabaseUserPrivilegesCommand command,
        string subject,
        Error error,
        CancellationToken cancellationToken)
    {
        await _journal.RecordFailureAsync(
            DatabaseAuditActions.DatabasePrivilegesChanged, subject, command.IpAddress, command.UserAgent, cancellationToken);

        return Result.Fail(error);
    }
}
