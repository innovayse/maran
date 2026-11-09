using Maran.Agent.Client.Interfaces;
using Maran.Modules.Mail.Domain.Policies;
using Maran.Modules.Mail.Extensions;
using Maran.Modules.Mail.Persistence;
using Maran.Modules.Mail.Resources;
using Maran.Modules.Mail.Services;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Mail.Commands.DeleteMailbox;

/// <summary>
/// Handles <see cref="DeleteMailboxCommand"/>: removes the mailbox and its mail from the host, then its
/// autoresponder and the row that owned it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Which mailbox is removed is decided by the panel's row and by nothing else</b>, and the refusal for
/// somebody else's row is indistinguishable from the refusal for a row that does not exist: the row is
/// loaded through the tenant-filtered context, so another account's identifier finds nothing
/// (rules/security.md item 6). The agent is then addressed by the owning account's system user name and
/// the domain name from that row, never by anything the caller typed.
/// </para>
/// <para>
/// <b>The agent runs first</b>, and a host that no longer knows the mailbox (<c>AgentNotFound</c>) is
/// accepted as done: the end state the caller asked for already holds, and refusing would leave a row
/// that can never be removed. A row removed with the mailbox still on the host would be mail nobody in
/// the panel can see or delete, which is the worse half. If the row cannot be removed after the host
/// deleted the data, nothing can be put back; the request is simply repeated and converges, because the
/// second host call answers not found.
/// </para>
/// </remarks>
public sealed class DeleteMailboxCommandHandler
{
    /// <summary>The agent client's stable code for "the host does not hold this".</summary>
    private const string AgentNotFoundCode = "AgentNotFound";

    /// <summary>The Mail module's database context, and this module's tenant boundary.</summary>
    private readonly MailDbContext _dbContext;

    /// <summary>Tells whether mail is switched on for the server.</summary>
    private readonly MailServerReader _server;

    /// <summary>The one window onto the owning account's system user name.</summary>
    private readonly IAccountDirectory _accounts;

    /// <summary>The agent, which owns everything that exists on the host.</summary>
    private readonly IAgentMailClient _agent;

    /// <summary>This module's audit journal.</summary>
    private readonly MailAuditJournal _journal;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Mail module's database context.</param>
    /// <param name="server">Tells whether mail is switched on.</param>
    /// <param name="accounts">The owning account's system user name.</param>
    /// <param name="agent">The agent client that removes the mailbox.</param>
    /// <param name="journal">This module's audit journal.</param>
    public DeleteMailboxCommandHandler(
        MailDbContext dbContext,
        MailServerReader server,
        IAccountDirectory accounts,
        IAgentMailClient agent,
        MailAuditJournal journal)
    {
        _dbContext = dbContext;
        _server = server;
        _accounts = accounts;
        _agent = agent;
        _journal = journal;
    }

    /// <summary>Removes the mailbox.</summary>
    /// <param name="command">Which mailbox to remove.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    /// Success — or <c>MailNotEnabled</c>, <c>MailboxNotFound</c>, <c>AccountNotFound</c> or the agent's own
    /// typed failure.
    /// </returns>
    public async Task<Result> HandleAsync(DeleteMailboxCommand command, CancellationToken cancellationToken)
    {
        if (await _server.FindEnabledAsync(cancellationToken) is null)
        {
            return await FailAsync(
                command,
                command.MailboxId.ToString(),
                Error.Of(nameof(ErrorMessages.MailNotEnabled), ErrorType.Conflict),
                cancellationToken);
        }

        var mailbox = await _dbContext.Mailboxes.FindWithDomainAsync(command.MailboxId, cancellationToken);
        if (mailbox is null)
        {
            // The subject is the identifier the caller supplied, because no address is known: a probe for a
            // mailbox the caller may not see still leaves a trace naming what was probed for.
            return await FailAsync(
                command,
                command.MailboxId.ToString(),
                Error.Of(nameof(ErrorMessages.MailboxNotFound), ErrorType.NotFound),
                cancellationToken);
        }

        var address = MailboxAddressPolicy.Compose(mailbox.LocalPart, mailbox.MailDomain.Name);

        var account = await _accounts.FindAsync(mailbox.AccountId, cancellationToken);
        if (account is null)
        {
            return await FailAsync(
                command,
                address,
                Error.Of(nameof(ErrorMessages.AccountNotFound), ErrorType.NotFound),
                cancellationToken);
        }

        var removed = await _agent.RemoveMailboxAsync(
            account.Username, mailbox.MailDomain.Name, mailbox.LocalPart, deleteData: true, cancellationToken);
        if (!removed.IsSuccess && !string.Equals(removed.Error!.Code, AgentNotFoundCode, StringComparison.Ordinal))
        {
            return await FailAsync(command, address, removed.Error!, cancellationToken);
        }

        var responder = await _dbContext.MailAutoresponders
            .SingleOrDefaultAsync(candidate => candidate.MailboxId == mailbox.Id, cancellationToken);
        if (responder is not null)
        {
            _dbContext.MailAutoresponders.Remove(responder);
        }

        _dbContext.Mailboxes.Remove(mailbox);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _journal.RecordSuccessAsync(
            HostedMailAuditActions.MailboxDeleted, address, command.IpAddress, command.UserAgent, cancellationToken);

        return Result.Ok();
    }

    /// <summary>Journals a refused delete and returns it as the typed failure.</summary>
    /// <param name="command">The delete that was refused.</param>
    /// <param name="subject">The address, or the supplied identifier when no row was found.</param>
    /// <param name="error">The typed failure to answer with, code and kind together.</param>
    /// <param name="cancellationToken">Cancels the journal write.</param>
    /// <returns>The failed result carrying <paramref name="error"/>.</returns>
    private async Task<Result> FailAsync(
        DeleteMailboxCommand command,
        string subject,
        Error error,
        CancellationToken cancellationToken)
    {
        await _journal.RecordFailureAsync(
            HostedMailAuditActions.MailboxDeleted, subject, command.IpAddress, command.UserAgent, cancellationToken);

        return Result.Fail(error);
    }
}
