using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.MailService;
using Maran.Modules.Mail.Common;
using Maran.Modules.Mail.Domain.Entities;
using Maran.Modules.Mail.Domain.Policies;
using Maran.Modules.Mail.Extensions;
using Maran.Modules.Mail.Mappers;
using Maran.Modules.Mail.Persistence;
using Maran.Modules.Mail.Resources;
using Maran.Modules.Mail.Services;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Microsoft.Extensions.Logging;

namespace Maran.Modules.Mail.Commands.SetMailboxAutoresponder;

/// <summary>
/// Handles <see cref="SetMailboxAutoresponderCommand"/>: installs the reply on the host, then records it,
/// and puts the previous reply back if the row refuses.
/// </summary>
/// <remarks>
/// <para>
/// <b>The mailbox is found through the tenant-filtered context</b>, so another account's mailbox answers
/// <c>MailboxNotFound</c> (rules/security.md item 6); the autoresponder row is scoped through its mailbox
/// by its own query filter, so a second door to it does not exist.
/// </para>
/// <para>
/// <b>The agent runs first</b> and the row is an upsert: the mailbox's one reply is replaced when it
/// exists and created when it does not. If the row cannot be written after the host accepted the reply,
/// the reply the mailbox had before (or none) is sent back, because a host answering mail with text the
/// panel does not show is a customer who cannot find out why.
/// </para>
/// <para>
/// The text was validated by <see cref="SetMailboxAutoresponderCommandValidator"/> and is validated again
/// by the agent; this handler neither trusts nor re-judges it.
/// </para>
/// </remarks>
public sealed class SetMailboxAutoresponderCommandHandler
{
    /// <summary>Pre-compiled log delegate for a restore that did not take.</summary>
    /// <remarks>Names the address and the agent's error CODE only — never the reply's text.</remarks>
    private static readonly Action<ILogger, string, string, Exception?> LogRestoreFailed =
        LoggerMessage.Define<string, string>(
            LogLevel.Error,
            new EventId(1, nameof(SetMailboxAutoresponderCommandHandler)),
            "Mailbox {Address} has an automatic reply on the mail host that the panel could not record, and the "
            + "previous one could not be put back ({AgentErrorCode}); host and panel now disagree.");

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

    /// <summary>Where a failed restore is reported.</summary>
    private readonly ILogger<SetMailboxAutoresponderCommandHandler> _logger;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Mail module's database context.</param>
    /// <param name="server">Tells whether mail is switched on.</param>
    /// <param name="accounts">The owning account's system user name.</param>
    /// <param name="agent">The agent client that installs the reply.</param>
    /// <param name="journal">This module's audit journal.</param>
    /// <param name="logger">Where a failed restore is reported.</param>
    public SetMailboxAutoresponderCommandHandler(
        MailDbContext dbContext,
        MailServerReader server,
        IAccountDirectory accounts,
        IAgentMailClient agent,
        MailAuditJournal journal,
        ILogger<SetMailboxAutoresponderCommandHandler> logger)
    {
        _dbContext = dbContext;
        _server = server;
        _accounts = accounts;
        _agent = agent;
        _journal = journal;
        _logger = logger;
    }

    /// <summary>Sets the mailbox's automatic reply.</summary>
    /// <param name="command">
    /// The validated parameters; see <see cref="SetMailboxAutoresponderCommandValidator"/>.
    /// </param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    /// The reply as stored — or <c>MailNotEnabled</c>, <c>MailboxNotFound</c>, <c>AccountNotFound</c>,
    /// <c>MailboxProvisioningFailed</c> or the agent's own typed failure.
    /// </returns>
    public async Task<Result<MailAutoresponderDto>> HandleAsync(
        SetMailboxAutoresponderCommand command,
        CancellationToken cancellationToken)
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

        var existing = await _dbContext.MailAutoresponders
            .SingleOrDefaultAsync(candidate => candidate.MailboxId == mailbox.Id, cancellationToken);
        var previous = existing is null ? null : MailAutoresponderMapper.ToAgent(existing);

        var installed = await _agent.SetAutoresponderAsync(
            account.Username,
            mailbox.MailDomain.Name,
            mailbox.LocalPart,
            new AgentMailAutoresponder(
                command.Subject,
                command.Body,
                command.StartsAt,
                command.EndsAt,
                (uint?)command.ReplyIntervalDays),
            cancellationToken);
        if (!installed.IsSuccess)
        {
            return await FailAsync(command, address, installed.Error!, cancellationToken);
        }

        var responder = existing;
        if (responder is null)
        {
            responder = new MailAutoresponder(
                Guid.NewGuid(),
                mailbox.Id,
                command.Subject,
                command.Body,
                command.StartsAt,
                command.EndsAt,
                command.ReplyIntervalDays);
            _dbContext.MailAutoresponders.Add(responder);
        }
        else
        {
            responder.Replace(
                command.Subject, command.Body, command.StartsAt, command.EndsAt, command.ReplyIntervalDays);
        }

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            Revert(responder, previous);
            await RestoreHostAsync(account, mailbox, previous, address, cancellationToken);

            return await FailAsync(
                command,
                address,
                Error.Of(nameof(ErrorMessages.MailboxProvisioningFailed), ErrorType.Failure),
                cancellationToken);
        }

        await _journal.RecordSuccessAsync(
            HostedMailAuditActions.MailAutoresponderSet,
            address,
            command.IpAddress,
            command.UserAgent,
            cancellationToken);

        return Result<MailAutoresponderDto>.Ok(MailAutoresponderMapper.From(responder));
    }

    /// <summary>Puts the tracked row back to what it held, or drops the one this request added.</summary>
    /// <param name="responder">The row the request wrote.</param>
    /// <param name="previous">
    /// What the row held, in the agent's terms; <c>null</c> when the mailbox had none and the request added it.
    /// </param>
    private void Revert(MailAutoresponder responder, AgentMailAutoresponder? previous)
    {
        if (previous is null)
        {
            _dbContext.MailAutoresponders.Remove(responder);

            return;
        }

        responder.Replace(
            previous.Subject,
            previous.Body,
            previous.StartsAt,
            previous.EndsAt,
            (int?)previous.ReplyIntervalDays);
    }

    /// <summary>Sends the host the reply the mailbox had before, or clears it when it had none.</summary>
    /// <param name="account">The owning account, whose system user name addresses the call.</param>
    /// <param name="mailbox">The mailbox.</param>
    /// <param name="previous">The previous reply, or <c>null</c> for none.</param>
    /// <param name="address">The address, for the log line.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    private async Task RestoreHostAsync(
        AccountSnapshot account,
        Mailbox mailbox,
        AgentMailAutoresponder? previous,
        string address,
        CancellationToken cancellationToken)
    {
        var restored = await _agent.SetAutoresponderAsync(
            account.Username, mailbox.MailDomain.Name, mailbox.LocalPart, previous, cancellationToken);
        if (!restored.IsSuccess)
        {
            LogRestoreFailed(_logger, address, restored.Error!.Code, null);
        }
    }

    /// <summary>Journals a refused change and returns it as the typed failure.</summary>
    /// <param name="command">The change that was refused.</param>
    /// <param name="subject">The address, or the mailbox identifier when no address could be formed.</param>
    /// <param name="error">The typed failure to answer with, code and kind together.</param>
    /// <param name="cancellationToken">Cancels the journal write.</param>
    /// <returns>The failed result carrying <paramref name="error"/>.</returns>
    private async Task<Result<MailAutoresponderDto>> FailAsync(
        SetMailboxAutoresponderCommand command,
        string subject,
        Error error,
        CancellationToken cancellationToken)
    {
        await _journal.RecordFailureAsync(
            HostedMailAuditActions.MailAutoresponderSet,
            subject,
            command.IpAddress,
            command.UserAgent,
            cancellationToken);

        return Result<MailAutoresponderDto>.Fail(error);
    }
}
