using Maran.Agent.Client.Interfaces;
using Maran.Modules.Mail.Common;
using Maran.Modules.Mail.Domain.Entities;
using Maran.Modules.Mail.Domain.Enums;
using Maran.Modules.Mail.Domain.Policies;
using Maran.Modules.Mail.Extensions;
using Maran.Modules.Mail.Interfaces;
using Maran.Modules.Mail.Mappers;
using Maran.Modules.Mail.Persistence;
using Maran.Modules.Mail.Resources;
using Maran.Modules.Mail.Services;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;

namespace Maran.Modules.Mail.Commands.UpdateMailbox;

/// <summary>
/// Handles <see cref="UpdateMailboxCommand"/>: changes a mailbox's limits and switch on the host first,
/// then in the row, and puts the host back if the row refuses.
/// </summary>
/// <remarks>
/// <para>
/// <b>The mailbox is found through the tenant-filtered context</b>, so another account's mailbox answers
/// <c>MailboxNotFound</c>, indistinguishable from a mailbox that does not exist (rules/security.md item 6).
/// </para>
/// <para>
/// <b>The agent runs first, with no password hash</b> — the agent's "keep the current one" — so the host
/// never holds a state the row has not been allowed to follow for longer than this call. The quota pool is
/// then taken under the per-account lock (<see cref="IMailboxSlotGate"/>), which also writes the new
/// limits; the owner's switch is applied to the tracked row first, so that one save carries both and the
/// row can never hold new limits with the old switch. If the gate refuses, the OLD values are sent to the
/// agent again and the row is put back as it was.
/// </para>
/// <para>
/// A request to raise a limit above the plan's is refused here, before the host is touched, and
/// <c>MailboxSendLimitAbovePlan</c> is a different sentence from <c>MailboxSendLimitOutOfRange</c>: the
/// first is about the plan, the second about the value.
/// </para>
/// </remarks>
public sealed class UpdateMailboxCommandHandler
{
    /// <summary>Pre-compiled log delegate for a restore that did not take.</summary>
    /// <remarks>Names the address and the agent's error CODE only; it is what an operator searches for.</remarks>
    private static readonly Action<ILogger, string, string, Exception?> LogRestoreFailed =
        LoggerMessage.Define<string, string>(
            LogLevel.Error,
            new EventId(1, nameof(UpdateMailboxCommandHandler)),
            "Mailbox {Address} was changed on the mail host but the row refused the change, and the previous "
            + "values could not be sent back ({AgentErrorCode}); the host and the panel now disagree.");

    /// <summary>The Mail module's database context, and this module's tenant boundary.</summary>
    private readonly MailDbContext _dbContext;

    /// <summary>Tells whether mail is switched on for the server.</summary>
    private readonly MailServerReader _server;

    /// <summary>The one window onto the owning account's system user name and plan allowances.</summary>
    private readonly IAccountDirectory _accounts;

    /// <summary>The agent, which owns everything that exists on the host.</summary>
    private readonly IAgentMailClient _agent;

    /// <summary>The atomic resize of the account's quota pool.</summary>
    private readonly IMailboxSlotGate _slotGate;

    /// <summary>This module's audit journal.</summary>
    private readonly MailAuditJournal _journal;

    /// <summary>The module's display names, which name the mailbox's status.</summary>
    private readonly IStringLocalizer<DisplayNames> _names;

    /// <summary>Where a failed restore is reported.</summary>
    private readonly ILogger<UpdateMailboxCommandHandler> _logger;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Mail module's database context.</param>
    /// <param name="server">Tells whether mail is switched on.</param>
    /// <param name="accounts">The owning account's system user name and plan allowances.</param>
    /// <param name="agent">The agent client that changes the mailbox.</param>
    /// <param name="slotGate">Resizes the account's pool atomically when the row is written.</param>
    /// <param name="journal">This module's audit journal.</param>
    /// <param name="names">The module's display names.</param>
    /// <param name="logger">Where a failed restore is reported.</param>
    public UpdateMailboxCommandHandler(
        MailDbContext dbContext,
        MailServerReader server,
        IAccountDirectory accounts,
        IAgentMailClient agent,
        IMailboxSlotGate slotGate,
        MailAuditJournal journal,
        IStringLocalizer<DisplayNames> names,
        ILogger<UpdateMailboxCommandHandler> logger)
    {
        _dbContext = dbContext;
        _server = server;
        _accounts = accounts;
        _agent = agent;
        _slotGate = slotGate;
        _journal = journal;
        _names = names;
        _logger = logger;
    }

    /// <summary>Changes the mailbox, refusing before the host is touched when anything says no.</summary>
    /// <param name="command">The validated parameters; see <see cref="UpdateMailboxCommandValidator"/>.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    /// The mailbox as stored — or <c>MailNotEnabled</c>, <c>MailboxNotFound</c>, <c>AccountNotFound</c>,
    /// <c>MailboxSendLimitAbovePlan</c>, <c>MailQuotaPoolExhausted</c>, <c>MailboxProvisioningFailed</c> or
    /// the agent's own typed failure.
    /// </returns>
    public async Task<Result<MailboxDto>> HandleAsync(
        UpdateMailboxCommand command,
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

        if (command.SendLimitPerHour > account.MailLimits.MailboxSendLimitPerHour)
        {
            return await FailAsync(
                command,
                address,
                Error.Of(nameof(ErrorMessages.MailboxSendLimitAbovePlan), ErrorType.Conflict),
                cancellationToken);
        }

        // Captured before anything changes: it is what the host and the row are put back to.
        var previous = (mailbox.QuotaMib, mailbox.SendLimitPerHour, mailbox.Enabled);

        var changed = await _agent.SetMailboxAsync(
            MailboxAgentMapper.Replacing(
                account.Username,
                mailbox.MailDomain.Name,
                mailbox,
                null,
                command.QuotaMib,
                command.SendLimitPerHour,
                command.Enabled),
            cancellationToken);
        if (!changed.IsSuccess)
        {
            return await FailAsync(command, address, changed.Error!, cancellationToken);
        }

        var refusal = await ApplyAsync(mailbox, account, command, cancellationToken);
        if (refusal is not null)
        {
            Restore(mailbox, previous);
            await RestoreHostAsync(account, mailbox, address, cancellationToken);

            return await FailAsync(command, address, refusal, cancellationToken);
        }

        await _journal.RecordSuccessAsync(
            HostedMailAuditActions.MailboxUpdated, address, command.IpAddress, command.UserAgent, cancellationToken);

        var ownerNames = await _accounts.NamesOfAsync([mailbox.AccountId], cancellationToken);
        var status = MailAccountStates.MailboxStatusOf(mailbox.Enabled, account.State == AccountState.Suspended);

        return Result<MailboxDto>.Ok(
            MailboxMapper.From(mailbox, mailbox.MailDomain.Name, ownerNames, null, status, _names));
    }

    /// <summary>Writes the new switch and limits to the row, under the account's pool lock.</summary>
    /// <param name="mailbox">The tracked row.</param>
    /// <param name="account">The owning account, whose plan decides the pool.</param>
    /// <param name="command">The change being applied.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns><c>null</c> when the row took the change; otherwise the refusal to answer with.</returns>
    private async Task<Error?> ApplyAsync(
        Mailbox mailbox,
        AccountSnapshot account,
        UpdateMailboxCommand command,
        CancellationToken cancellationToken)
    {
        if (command.Enabled)
        {
            mailbox.Reactivate();
        }
        else
        {
            mailbox.Disable();
        }

        try
        {
            var outcome = await _slotGate.TryResizeAsync(
                mailbox,
                command.QuotaMib,
                command.SendLimitPerHour,
                account.MailLimits.MailboxQuotaPoolMib,
                cancellationToken);

            // A resize can only be refused for the pool: the account's mailbox count does not change.
            return outcome == MailboxSlotOutcome.Taken
                ? null
                : Error.Of(nameof(ErrorMessages.MailQuotaPoolExhausted), ErrorType.Conflict);
        }
        catch (DbUpdateException)
        {
            return Error.Of(nameof(ErrorMessages.MailboxProvisioningFailed), ErrorType.Failure);
        }
    }

    /// <summary>Puts the tracked row back to the values it held before the request.</summary>
    /// <param name="mailbox">The tracked row.</param>
    /// <param name="previous">The quota, send limit and switch the row held.</param>
    private static void Restore(Mailbox mailbox, (int QuotaMib, int SendLimitPerHour, bool Enabled) previous)
    {
        mailbox.ChangeLimits(previous.QuotaMib, previous.SendLimitPerHour);
        if (previous.Enabled)
        {
            mailbox.Reactivate();
        }
        else
        {
            mailbox.Disable();
        }
    }

    /// <summary>Sends the host the values the row holds again, after the row refused the change.</summary>
    /// <param name="account">The owning account, whose system user name addresses the call.</param>
    /// <param name="mailbox">The row, already restored to its previous values.</param>
    /// <param name="address">The address, for the log line.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    private async Task RestoreHostAsync(
        AccountSnapshot account,
        Mailbox mailbox,
        string address,
        CancellationToken cancellationToken)
    {
        var restored = await _agent.SetMailboxAsync(
            MailboxAgentMapper.Current(account.Username, mailbox.MailDomain.Name, mailbox, null),
            cancellationToken);
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
    private async Task<Result<MailboxDto>> FailAsync(
        UpdateMailboxCommand command,
        string subject,
        Error error,
        CancellationToken cancellationToken)
    {
        await _journal.RecordFailureAsync(
            HostedMailAuditActions.MailboxUpdated, subject, command.IpAddress, command.UserAgent, cancellationToken);

        return Result<MailboxDto>.Fail(error);
    }
}
