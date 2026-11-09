using Maran.Agent.Client.Interfaces;
using Maran.Modules.Databases.Common;
using Maran.Modules.Databases.Persistence;
using Maran.Modules.Databases.Resources;
using Maran.Modules.Databases.Services;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Maran.SharedKernel.Security;

namespace Maran.Modules.Databases.Commands.ResetDatabaseUserPassword;

/// <summary>
/// Handles <see cref="ResetDatabaseUserPasswordCommand"/>: mints a password, sets it on the server and
/// returns it once.
/// </summary>
/// <remarks>
/// Works for a dedicated user as well as an additional one, the same login being re-credentialed either
/// way. The journal records the user's name and never the value: an entry recording which password was
/// set would be the journal keeping the copy this module takes such trouble not to keep.
/// </remarks>
public sealed class ResetDatabaseUserPasswordCommandHandler
{
    /// <summary>The Databases module's database context, and this module's tenant boundary.</summary>
    private readonly DatabasesDbContext _dbContext;

    /// <summary>The owning account's system user name.</summary>
    private readonly IAccountDirectory _accounts;

    /// <summary>The agent, which owns everything that exists on the MySQL server.</summary>
    private readonly IAgentDbClient _agent;

    /// <summary>This module's audit journal.</summary>
    private readonly DatabaseAuditJournal _journal;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Databases module's database context.</param>
    /// <param name="accounts">The owning account's system user name.</param>
    /// <param name="agent">The agent client that sets the password.</param>
    /// <param name="journal">This module's audit journal.</param>
    public ResetDatabaseUserPasswordCommandHandler(
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

    /// <summary>Replaces the user's password.</summary>
    /// <param name="command">Which user to re-credential.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The new password, once, or <c>DatabaseUserNotFound</c>, <c>AccountNotFound</c>, or the agent's own typed failure.</returns>
    public async Task<Result<DatabaseUserPasswordDto>> HandleAsync(
        ResetDatabaseUserPasswordCommand command,
        CancellationToken cancellationToken)
    {
        var user = await _dbContext.DatabaseUsers
            .AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == command.UserId, cancellationToken);
        if (user is null)
        {
            return await FailAsync(
                command,
                command.UserId.ToString(),
                Error.Of(nameof(ErrorMessages.DatabaseUserNotFound), ErrorType.NotFound),
                cancellationToken);
        }

        var account = await _accounts.FindAsync(user.AccountId, cancellationToken);
        if (account is null)
        {
            return await FailAsync(
                command, user.FullName, Error.Of(nameof(ErrorMessages.AccountNotFound), ErrorType.NotFound), cancellationToken);
        }

        var password = ProvisionedPasswordGenerator.Generate();

        var installed = await _agent.SetPasswordAsync(account.Username, user.NameSuffix, password, cancellationToken);
        if (!installed.IsSuccess)
        {
            return await FailAsync(command, user.FullName, installed.Error!, cancellationToken);
        }

        await _journal.RecordSuccessAsync(
            DatabaseAuditActions.DatabaseUserPasswordReset,
            user.FullName,
            command.IpAddress,
            command.UserAgent,
            cancellationToken);

        return Result<DatabaseUserPasswordDto>.Ok(new DatabaseUserPasswordDto(user.Id, user.FullName, password));
    }

    /// <summary>Journals a refused reset and returns it as the typed failure.</summary>
    /// <param name="command">The reset that was refused.</param>
    /// <param name="subject">The user's full name, or the supplied identifier when no row was found.</param>
    /// <param name="error">The typed failure to answer with.</param>
    /// <param name="cancellationToken">Cancels the journal write.</param>
    /// <returns>The failed result carrying <paramref name="error"/>.</returns>
    private async Task<Result<DatabaseUserPasswordDto>> FailAsync(
        ResetDatabaseUserPasswordCommand command,
        string subject,
        Error error,
        CancellationToken cancellationToken)
    {
        await _journal.RecordFailureAsync(
            DatabaseAuditActions.DatabaseUserPasswordReset, subject, command.IpAddress, command.UserAgent, cancellationToken);

        return Result<DatabaseUserPasswordDto>.Fail(error);
    }
}
