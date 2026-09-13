using Maran.Agent.Client.Interfaces;
using Maran.Modules.Sftp.Persistence;
using Maran.Modules.Sftp.Resources;
using Maran.Modules.Sftp.Services;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Sftp.Commands.DeleteSftpUser;

/// <summary>
/// Handles <see cref="DeleteSftpUserCommand"/>: removes the login from the host, and then the row
/// that owned it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Which login is removed is decided by the panel's row and by nothing else.</b> The row is
/// loaded through the tenant-filtered context, so another customer's identifier finds nothing and
/// answers 404 — never 403, which would confirm the login exists. The agent is then addressed by the
/// SUFFIX this row recorded, so the delete cannot reach past the account even if the row were wrong:
/// the agent applies the account prefix itself.
/// </para>
/// <para>
/// The agent runs first, as everywhere in this module. A login removed with the row still present is
/// visible, retryable and converges — the agent reports a second delete as <c>NotFound</c>. A row
/// removed with the login still there is a live credential into a customer's home that nobody in the
/// panel can see and nobody can now revoke, which is the worse half by a wide margin: the customer
/// asked for exactly that access to end.
/// </para>
/// </remarks>
public sealed class DeleteSftpUserCommandHandler
{
    /// <summary>The Sftp module's database context, and this module's tenant boundary.</summary>
    private readonly SftpDbContext _dbContext;

    /// <summary>The owning account's system user name, which addresses every agent operation.</summary>
    private readonly IAccountDirectory _accounts;

    /// <summary>The agent, which owns everything that exists on the host.</summary>
    private readonly IAgentSftpClient _agent;

    /// <summary>This module's audit journal.</summary>
    private readonly SftpAuditJournal _journal;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Sftp module's database context.</param>
    /// <param name="accounts">The owning account's system user name.</param>
    /// <param name="agent">The agent client that removes the login.</param>
    /// <param name="journal">This module's audit journal.</param>
    public DeleteSftpUserCommandHandler(
        SftpDbContext dbContext,
        IAccountDirectory accounts,
        IAgentSftpClient agent,
        SftpAuditJournal journal)
    {
        _dbContext = dbContext;
        _accounts = accounts;
        _agent = agent;
        _journal = journal;
    }

    /// <summary>Removes the login. Idempotent from the customer's side: a second attempt is not found.</summary>
    /// <param name="command">Which login to remove.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Success, or <c>SftpUserNotFound</c>, <c>AccountNotFound</c>, or the agent's own typed failure.</returns>
    public async Task<Result<bool>> HandleAsync(DeleteSftpUserCommand command, CancellationToken cancellationToken)
    {
        var sftpUser = await _dbContext.SftpUsers
            .SingleOrDefaultAsync(row => row.Id == command.SftpUserId, cancellationToken);
        if (sftpUser is null)
        {
            // The subject is the identifier the caller supplied, because no name is known — a probe
            // for a login the caller may not see still leaves a trace naming what was probed for.
            return await FailAsync(
                command,
                command.SftpUserId.ToString(),
                Error.Of(nameof(ErrorMessages.SftpUserNotFound), ErrorType.NotFound),
                cancellationToken);
        }

        var account = await _accounts.FindAsync(sftpUser.AccountId, cancellationToken);
        if (account is null)
        {
            return await FailAsync(
                command, sftpUser.Name, Error.Of(nameof(ErrorMessages.AccountNotFound), ErrorType.NotFound), cancellationToken);
        }

        var deleted = await _agent.DeleteAsync(account.Username, sftpUser.Name, cancellationToken);
        if (!deleted.IsSuccess)
        {
            return await FailAsync(command, sftpUser.Name, deleted.Error!, cancellationToken);
        }

        // Captured before the row goes: the journal records the name, which is the only thing about
        // a removed login anybody will later be able to search for.
        var name = sftpUser.Name;

        _dbContext.SftpUsers.Remove(sftpUser);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _journal.RecordSuccessAsync(
            AuditActions.SftpUserDeleted, name, command.IpAddress, command.UserAgent, cancellationToken);

        return Result<bool>.Ok(true);
    }

    /// <summary>Journals a refused delete and returns it as the typed failure.</summary>
    /// <param name="command">The delete that was refused.</param>
    /// <param name="subject">The login's name, or the supplied identifier when no row was found.</param>
    /// <param name="error">The typed failure to answer with, code and kind together.</param>
    /// <param name="cancellationToken">Cancels the journal write.</param>
    /// <returns>The failed result carrying <paramref name="error"/>.</returns>
    private async Task<Result<bool>> FailAsync(
        DeleteSftpUserCommand command,
        string subject,
        Error error,
        CancellationToken cancellationToken)
    {
        await _journal.RecordFailureAsync(
            AuditActions.SftpUserDeleted, subject, command.IpAddress, command.UserAgent, cancellationToken);

        return Result<bool>.Fail(error);
    }
}
