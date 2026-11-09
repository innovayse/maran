using Maran.Agent.Client.Interfaces;
using Maran.Modules.Mail.Domain.Policies;
using Maran.Modules.Mail.Extensions;
using Maran.Modules.Mail.Persistence;
using Maran.Modules.Mail.Resources;
using Maran.Modules.Mail.Services;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Mail.Commands.ClearMailboxAutoresponder;

/// <summary>
/// Handles <see cref="ClearMailboxAutoresponderCommand"/>: removes the reply from the host, then its row.
/// </summary>
/// <remarks>
/// <para>
/// <b>The mailbox is found through the tenant-filtered context</b>, so another account's mailbox answers
/// <c>MailboxNotFound</c> (rules/security.md item 6).
/// </para>
/// <para>
/// <b>Idempotent by construction.</b> The agent is always told to clear, whether or not a row exists, so
/// a reply the host holds without a row (a row lost to an earlier failure) is cleared too; a mailbox with
/// no reply simply ends in the state it already was in. The journal records
/// <c>MailAutoresponderCleared</c>, an action of its own (setting a reply is <c>MailAutoresponderSet</c>),
/// so one action name keeps one meaning; the entry's subject is the address.
/// </para>
/// </remarks>
public sealed class ClearMailboxAutoresponderCommandHandler
{
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
    /// <param name="agent">The agent client that clears the reply.</param>
    /// <param name="journal">This module's audit journal.</param>
    public ClearMailboxAutoresponderCommandHandler(
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

    /// <summary>Clears the mailbox's automatic reply.</summary>
    /// <param name="command">Which mailbox to clear.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    /// Success — or <c>MailNotEnabled</c>, <c>MailboxNotFound</c>, <c>AccountNotFound</c> or the agent's own
    /// typed failure.
    /// </returns>
    public async Task<Result> HandleAsync(ClearMailboxAutoresponderCommand command, CancellationToken cancellationToken)
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

        var cleared = await _agent.SetAutoresponderAsync(
            account.Username, mailbox.MailDomain.Name, mailbox.LocalPart, null, cancellationToken);
        if (!cleared.IsSuccess)
        {
            return await FailAsync(command, address, cleared.Error!, cancellationToken);
        }

        var responder = await _dbContext.MailAutoresponders
            .SingleOrDefaultAsync(candidate => candidate.MailboxId == mailbox.Id, cancellationToken);
        if (responder is not null)
        {
            _dbContext.MailAutoresponders.Remove(responder);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        await _journal.RecordSuccessAsync(
            HostedMailAuditActions.MailAutoresponderCleared,
            address,
            command.IpAddress,
            command.UserAgent,
            cancellationToken);

        return Result.Ok();
    }

    /// <summary>Journals a refused clear and returns it as the typed failure.</summary>
    /// <param name="command">The clear that was refused.</param>
    /// <param name="subject">The address, or the mailbox identifier when no address could be formed.</param>
    /// <param name="error">The typed failure to answer with, code and kind together.</param>
    /// <param name="cancellationToken">Cancels the journal write.</param>
    /// <returns>The failed result carrying <paramref name="error"/>.</returns>
    private async Task<Result> FailAsync(
        ClearMailboxAutoresponderCommand command,
        string subject,
        Error error,
        CancellationToken cancellationToken)
    {
        await _journal.RecordFailureAsync(
            HostedMailAuditActions.MailAutoresponderCleared,
            subject,
            command.IpAddress,
            command.UserAgent,
            cancellationToken);

        return Result.Fail(error);
    }
}
