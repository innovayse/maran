using Maran.Agent.Client.Interfaces;
using Maran.Modules.Databases.Common;
using Maran.Modules.Databases.Persistence;
using Maran.Modules.Databases.Resources;
using Maran.Modules.Databases.Services;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Maran.SharedKernel.Security;

namespace Maran.Modules.Databases.Commands.ResetDatabasePassword;

/// <summary>
/// Handles <see cref="ResetDatabasePasswordCommand"/>: mints a new password, has the agent install
/// it on the database's dedicated user, and returns it once.
/// </summary>
/// <remarks>
/// <para>
/// <b>No row is written, and that is the whole design rather than an omission.</b> There is no
/// password column to update — see <c>Database</c> — so a reset changes nothing in PostgreSQL at
/// all. The server's own hash becomes the only copy of the new value the instant this method
/// returns, exactly as it was for the old one.
/// </para>
/// <para>
/// <b>Tenant-scoped like every other command here, and by the same single mechanism.</b> The row is
/// loaded through the tenant-filtered context, so another customer's identifier finds nothing and
/// answers 404 rather than 403. That matters more on this command than on a read: a 403 would
/// confirm the database exists, and this is the one endpoint that, if it could be pointed at
/// somebody else's row, would hand the caller a working credential on their data rather than merely
/// telling them it is there.
/// </para>
/// <para>
/// The agent is addressed by the user-name SUFFIX the row recorded, never by the fully-qualified
/// name: the agent applies the account prefix itself, so a reset cannot express another tenant's
/// login even if the row it read were wrong.
/// </para>
/// <para>
/// There is nothing to compensate. The agent either installs the new password or it does not; if it
/// does not, the old one is still live and the customer is told the reset failed, which is true and
/// leaves them exactly where they were.
/// </para>
/// </remarks>
public sealed class ResetDatabasePasswordCommandHandler
{
    /// <summary>The Databases module's database context, and this module's tenant boundary.</summary>
    private readonly DatabasesDbContext _dbContext;

    /// <summary>The owning account's system user name, which addresses the agent call.</summary>
    private readonly IAccountDirectory _accounts;

    /// <summary>The agent, which owns everything that exists on the MySQL server.</summary>
    private readonly IAgentDbClient _agent;

    /// <summary>This module's audit journal.</summary>
    private readonly DatabaseAuditJournal _journal;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Databases module's database context.</param>
    /// <param name="accounts">The owning account's system user name.</param>
    /// <param name="agent">The agent client that installs the new password.</param>
    /// <param name="journal">This module's audit journal.</param>
    public ResetDatabasePasswordCommandHandler(
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

    /// <summary>Installs a new password and returns it once.</summary>
    /// <param name="command">Which database's user to re-credential.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    /// The new password, shown once — or <c>DatabaseNotFound</c>, <c>AccountNotFound</c>, or the
    /// agent's own typed failure.
    /// </returns>
    public async Task<Result<DatabasePasswordDto>> HandleAsync(
        ResetDatabasePasswordCommand command,
        CancellationToken cancellationToken)
    {
        var database = await _dbContext.Databases
            .AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == command.DatabaseId, cancellationToken);
        if (database is null)
        {
            // The subject is the identifier the caller supplied, because no name is known. A probe
            // for a database the caller may not see is exactly the pattern this journal exists to
            // make visible, and on this command more than on any other.
            return await FailAsync(
                command,
                command.DatabaseId.ToString(),
                Error.Of(nameof(ErrorMessages.DatabaseNotFound), ErrorType.NotFound),
                cancellationToken);
        }

        var account = await _accounts.FindAsync(database.AccountId, cancellationToken);
        if (account is null)
        {
            return await FailAsync(
                command, database.Name, Error.Of(nameof(ErrorMessages.AccountNotFound), ErrorType.NotFound), cancellationToken);
        }

        var password = ProvisionedPasswordGenerator.Generate();

        var installed = await _agent.SetPasswordAsync(
            account.Username, database.DbUserNameSuffix, password, cancellationToken);
        if (!installed.IsSuccess)
        {
            return await FailAsync(command, database.Name, installed.Error!, cancellationToken);
        }

        // The name, never the value. An entry recording which password was set would be the journal
        // keeping the copy this whole module takes such trouble not to keep — and the journal is
        // never deleted.
        await _journal.RecordSuccessAsync(
            AuditActions.DatabasePasswordReset,
            database.Name,
            command.IpAddress,
            command.UserAgent,
            cancellationToken);

        return Result<DatabasePasswordDto>.Ok(
            new DatabasePasswordDto(database.Id, database.DbUserName, password));
    }

    /// <summary>Journals a refused reset and returns it as the typed failure.</summary>
    /// <param name="command">The reset that was refused.</param>
    /// <param name="subject">The database's name, or the supplied identifier when no row was found.</param>
    /// <param name="error">The typed failure to answer with, code and kind together.</param>
    /// <param name="cancellationToken">Cancels the journal write.</param>
    /// <returns>The failed result carrying <paramref name="error"/>.</returns>
    private async Task<Result<DatabasePasswordDto>> FailAsync(
        ResetDatabasePasswordCommand command,
        string subject,
        Error error,
        CancellationToken cancellationToken)
    {
        await _journal.RecordFailureAsync(
            AuditActions.DatabasePasswordReset, subject, command.IpAddress, command.UserAgent, cancellationToken);

        return Result<DatabasePasswordDto>.Fail(error);
    }
}
