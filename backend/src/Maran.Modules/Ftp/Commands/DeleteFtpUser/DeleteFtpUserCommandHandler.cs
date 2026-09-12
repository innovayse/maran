using Maran.Agent.Client.Interfaces;
using Maran.Modules.Ftp.Persistence;
using Maran.Modules.Ftp.Resources;
using Maran.Modules.Ftp.Services;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Ftp.Commands.DeleteFtpUser;

/// <summary>
/// Handles <see cref="DeleteFtpUserCommand"/>: removes the login from the host, and then the row
/// that owned it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Which login is removed is decided by the panel's row and by nothing else, and the refusal for
/// somebody else's row is indistinguishable from the refusal for a row that does not exist.</b> The
/// row is loaded through the tenant-filtered context, so another customer's identifier finds nothing
/// — the handler is not choosing to hide it, the row genuinely is not in the result set — and the
/// single <c>FtpUserNotFound</c> answer covers both cases. A distinct refusal would confirm that the
/// identifier names a real login somebody else holds, which is the disclosure rules/security.md item
/// 6 refuses.
/// </para>
/// <para>
/// <b>The agent is then addressed by the ACCOUNT and the SUFFIX, never by the fully-qualified
/// login.</b> That is the second half of the same guarantee and it is held by a different mechanism:
/// the agent applies the prefix itself and confirms the candidate's passwd home is exactly that
/// account's jail before it removes anything, because <c>&lt;account&gt;_&lt;name&gt;</c> has no
/// unique decomposition when account names may carry the separator. A cross-tenant destructive
/// delete has already been found and fixed on this branch in both protocols; the panel's row filter
/// and the agent's home check each close it alone, and this handler must not be the hole that
/// reopens either — which is why it passes <c>account.Username</c> from the row's own account and
/// never a name the caller supplied.
/// </para>
/// <para>
/// The agent runs first, as everywhere in this module. A login removed with the row still present is
/// visible, retryable and converges — the agent reports a second delete as <c>NotFound</c>. A row
/// removed with the login still there is a live credential into a customer's home that nobody in the
/// panel can see and nobody can now revoke, which is the worse half by a wide margin: the customer
/// asked for exactly that access to end.
/// </para>
/// </remarks>
public sealed class DeleteFtpUserCommandHandler
{
    /// <summary>The Ftp module's database context, and this module's tenant boundary.</summary>
    private readonly FtpDbContext _dbContext;

    /// <summary>The owning account's system user name, which addresses every agent operation.</summary>
    private readonly IAccountDirectory _accounts;

    /// <summary>The agent, which owns everything that exists on the host.</summary>
    private readonly IAgentFtpsClient _agent;

    /// <summary>This module's audit journal.</summary>
    private readonly FtpAuditJournal _journal;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Ftp module's database context.</param>
    /// <param name="accounts">The owning account's system user name.</param>
    /// <param name="agent">The agent client that removes the login.</param>
    /// <param name="journal">This module's audit journal.</param>
    public DeleteFtpUserCommandHandler(
        FtpDbContext dbContext,
        IAccountDirectory accounts,
        IAgentFtpsClient agent,
        FtpAuditJournal journal)
    {
        _dbContext = dbContext;
        _accounts = accounts;
        _agent = agent;
        _journal = journal;
    }

    /// <summary>Removes the login. Idempotent from the customer's side: a second attempt is not found.</summary>
    /// <param name="command">Which login to remove.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Success, or <c>FtpUserNotFound</c>, <c>AccountNotFound</c>, or the agent's own typed failure.</returns>
    public async Task<Result<bool>> HandleAsync(DeleteFtpUserCommand command, CancellationToken cancellationToken)
    {
        var ftpUser = await _dbContext.FtpUsers
            .SingleOrDefaultAsync(row => row.Id == command.FtpUserId, cancellationToken);
        if (ftpUser is null)
        {
            // The subject is the identifier the caller supplied, because no name is known — a probe
            // for a login the caller may not see still leaves a trace naming what was probed for.
            // The journal is where a typo and a probe are told apart, since the ANSWER is the same.
            return await FailAsync(
                command,
                command.FtpUserId.ToString(),
                Error.Of(nameof(ErrorMessages.FtpUserNotFound), ErrorType.NotFound),
                cancellationToken);
        }

        // A row whose owning account the directory cannot resolve is answered here and never sent
        // on. The alternative this replaces addressed the agent with a placeholder user name, which
        // the agent could only answer NOT_FOUND to — and NOT_FOUND renders as "this no longer exists
        // on your server, refresh the page", a sentence that is wrong twice over: the login is still
        // there, and the refresh it advises brings back the very row the customer just failed to
        // remove. Worse, the placeholder is a legal account name, so on a server where somebody owns
        // it the panel would be asking the agent to delete a login in a NEIGHBOUR's namespace, and
        // only the agent's own home check stood between that and a cross-tenant deletion.
        // AccountNotFound names the actor correctly: nothing the customer typed is at fault, and the
        // thing that is missing is the account, not the login.
        var account = await _accounts.FindAsync(ftpUser.AccountId, cancellationToken);
        if (account is null)
        {
            return await FailAsync(
                command,
                ftpUser.Name,
                Error.Of(nameof(ErrorMessages.AccountNotFound), ErrorType.NotFound),
                cancellationToken);
        }

        var deleted = await _agent.DeleteUserAsync(account.Username, ftpUser.Name, cancellationToken);
        if (!deleted.IsSuccess)
        {
            return await FailAsync(command, ftpUser.Name, deleted.Error!, cancellationToken);
        }

        // Captured before the row goes: the journal records the name, which is the only thing about
        // a removed login anybody will later be able to search for.
        var name = ftpUser.Name;

        _dbContext.FtpUsers.Remove(ftpUser);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _journal.RecordSuccessAsync(
            FtpAuditJournal.FtpUserDeleted, name, command.IpAddress, command.UserAgent, cancellationToken);

        return Result<bool>.Ok(true);
    }

    /// <summary>Journals a refused delete and returns it as the typed failure.</summary>
    /// <param name="command">The delete that was refused.</param>
    /// <param name="subject">The login's name, or the supplied identifier when no row was found.</param>
    /// <param name="error">The typed failure to answer with, code and kind together.</param>
    /// <param name="cancellationToken">Cancels the journal write.</param>
    /// <returns>The failed result carrying <paramref name="error"/>.</returns>
    private async Task<Result<bool>> FailAsync(
        DeleteFtpUserCommand command,
        string subject,
        Error error,
        CancellationToken cancellationToken)
    {
        await _journal.RecordFailureAsync(
            FtpAuditJournal.FtpUserDeleted, subject, command.IpAddress, command.UserAgent, cancellationToken);

        return Result<bool>.Fail(error);
    }
}
