using Maran.Agent.Client.Interfaces;
using Maran.Modules.Mail.Persistence;
using Maran.Modules.Mail.Resources;
using Maran.Modules.Mail.Services;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Mail.Commands.DeleteMailDomain;

/// <summary>
/// Handles <see cref="DeleteMailDomainCommand"/>: removes every mailbox of the domain and then the
/// domain itself on the host, and only then deletes the rows.
/// </summary>
/// <remarks>
/// <para>
/// <b>The host first, the rows after.</b> If the host refuses part-way, the rows still describe what
/// is still there, and the same request can be repeated: every agent call here is idempotent, and
/// the agent's "not found" is read as "already gone" and passed over. The opposite order would leave
/// mailboxes on the host that no row could ever lead an operator back to.
/// </para>
/// <para>
/// <b>Mailbox data is deleted with the mailbox.</b> The domain's mail is the customer's to destroy
/// by deleting the domain; the confirmation is the interface's job, the data removal is this
/// handler's.
/// </para>
/// <para>
/// The domain is read through the tenant-filtered context, so another account's domain answers
/// <c>MailDomainNotFound</c> exactly as a domain that does not exist does.
/// </para>
/// </remarks>
public sealed class DeleteMailDomainCommandHandler
{
    /// <summary>The agent client's stable code for "the host no longer holds this".</summary>
    private const string AgentNotFoundCode = "AgentNotFound";

    /// <summary>The Mail module's database context, and this module's tenant boundary.</summary>
    private readonly MailDbContext _dbContext;

    /// <summary>The window onto the owning account's system user name.</summary>
    private readonly IAccountDirectory _accounts;

    /// <summary>The agent, which owns everything that exists on the host.</summary>
    private readonly IAgentMailClient _agent;

    /// <summary>This module's audit journal.</summary>
    private readonly MailAuditJournal _journal;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Mail module's database context.</param>
    /// <param name="accounts">The owning account's system user name.</param>
    /// <param name="agent">The agent client that removes the host state.</param>
    /// <param name="journal">This module's audit journal.</param>
    public DeleteMailDomainCommandHandler(
        MailDbContext dbContext,
        IAccountDirectory accounts,
        IAgentMailClient agent,
        MailAuditJournal journal)
    {
        _dbContext = dbContext;
        _accounts = accounts;
        _agent = agent;
        _journal = journal;
    }

    /// <summary>Deletes the domain and everything under it.</summary>
    /// <param name="command">The domain to delete.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Success, or <c>MailDomainNotFound</c>, <c>AccountNotFound</c> or the agent's own typed failure.</returns>
    public async Task<Result> HandleAsync(DeleteMailDomainCommand command, CancellationToken cancellationToken)
    {
        var domain = await _dbContext.MailDomains
            .Include(row => row.DkimKeys)
            .SingleOrDefaultAsync(row => row.Id == command.MailDomainId, cancellationToken);
        if (domain is null)
        {
            // The subject is the identifier the caller supplied, because no name is known: a probe
            // for a domain the caller may not see still leaves a trace naming what was probed for.
            return await FailAsync(
                command,
                command.MailDomainId.ToString(),
                Error.Of(nameof(ErrorMessages.MailDomainNotFound), ErrorType.NotFound),
                cancellationToken);
        }

        var account = await _accounts.FindAsync(domain.AccountId, cancellationToken);
        if (account is null)
        {
            return await FailAsync(
                command,
                domain.Name,
                Error.Of(nameof(ErrorMessages.AccountNotFound), ErrorType.NotFound),
                cancellationToken);
        }

        var mailboxes = await _dbContext.Mailboxes
            .Where(row => row.MailDomainId == domain.Id)
            .ToListAsync(cancellationToken);
        var mailboxIds = mailboxes
            .Select(row =>
            {
                return row.Id;
            })
            .ToList();
        var autoresponders = await _dbContext.MailAutoresponders
            .Where(row => mailboxIds.Contains(row.MailboxId))
            .ToListAsync(cancellationToken);
        var aliases = await _dbContext.MailAliases
            .Include(row => row.Targets)
            .Where(row => row.MailDomainId == domain.Id)
            .ToListAsync(cancellationToken);

        foreach (var mailbox in mailboxes)
        {
            var removedMailbox = await _agent.RemoveMailboxAsync(
                account.Username, domain.Name, mailbox.LocalPart, deleteData: true, cancellationToken);
            if (!removedMailbox.IsSuccess && !IsAlreadyGone(removedMailbox.Error!))
            {
                return await FailAsync(command, domain.Name, removedMailbox.Error!, cancellationToken);
            }
        }

        var removedDomain = await _agent.RemoveDomainAsync(account.Username, domain.Name, cancellationToken);
        if (!removedDomain.IsSuccess && !IsAlreadyGone(removedDomain.Error!))
        {
            return await FailAsync(command, domain.Name, removedDomain.Error!, cancellationToken);
        }

        // Captured before the rows go: the journal records the name, which is the only thing about a
        // removed domain anybody will later be able to search for.
        var name = domain.Name;

        _dbContext.MailAutoresponders.RemoveRange(autoresponders);
        foreach (var alias in aliases)
        {
            _dbContext.MailAliasTargets.RemoveRange(alias.Targets.ToList());
        }

        _dbContext.MailAliases.RemoveRange(aliases);
        _dbContext.Mailboxes.RemoveRange(mailboxes);
        _dbContext.DkimKeys.RemoveRange(domain.DkimKeys.ToList());
        _dbContext.MailDomains.Remove(domain);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _journal.RecordSuccessAsync(
            HostedMailAuditActions.MailDomainDeleted, name, command.IpAddress, command.UserAgent, cancellationToken);

        return Result.Ok();
    }

    /// <summary>Whether the agent's refusal only says the host no longer holds the thing.</summary>
    /// <param name="error">The typed failure the agent client produced.</param>
    /// <returns><c>true</c> for the agent's not-found code.</returns>
    private static bool IsAlreadyGone(Error error)
    {
        return string.Equals(error.Code, AgentNotFoundCode, StringComparison.Ordinal);
    }

    /// <summary>Journals a refused deletion and returns it as the typed failure.</summary>
    /// <param name="command">The deletion that was refused.</param>
    /// <param name="subject">The domain name, or the identifier probed for.</param>
    /// <param name="error">The typed failure to answer with, code and kind together.</param>
    /// <param name="cancellationToken">Cancels the journal write.</param>
    /// <returns>The failed result carrying <paramref name="error"/>.</returns>
    private async Task<Result> FailAsync(
        DeleteMailDomainCommand command,
        string subject,
        Error error,
        CancellationToken cancellationToken)
    {
        await _journal.RecordFailureAsync(
            HostedMailAuditActions.MailDomainDeleted, subject, command.IpAddress, command.UserAgent, cancellationToken);

        return Result.Fail(error);
    }
}
