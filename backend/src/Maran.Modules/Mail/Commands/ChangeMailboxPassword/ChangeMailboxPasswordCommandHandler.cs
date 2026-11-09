using Maran.Agent.Client.Interfaces;
using Maran.Modules.Mail.Domain.Entities;
using Maran.Modules.Mail.Domain.Policies;
using Maran.Modules.Mail.Extensions;
using Maran.Modules.Mail.Mappers;
using Maran.Modules.Mail.Persistence;
using Maran.Modules.Mail.Resources;
using Maran.Modules.Mail.Services;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Maran.SharedKernel.Security;
using Microsoft.Extensions.Logging;

namespace Maran.Modules.Mail.Commands.ChangeMailboxPassword;

/// <summary>
/// Handles <see cref="ChangeMailboxPasswordCommand"/>: installs the new password hash on the host, then
/// records it, and puts the old hash back if the row refuses.
/// </summary>
/// <remarks>
/// <para>
/// <b>The mailbox is found through the tenant-filtered context</b>, so another account's mailbox answers
/// <c>MailboxNotFound</c> (rules/security.md item 6). The limits and the switch are sent UNCHANGED:
/// this operation changes the credential and nothing else.
/// </para>
/// <para>
/// <b>The plaintext password stops at <see cref="MailPasswordHashing"/>.</b> The agent receives the
/// hash, the row stores the hash, the journal records the address and nothing else, and no log line is
/// written with the command. The agent runs first, so a host that refuses the new credential leaves the
/// row — and the customer's working password — untouched.
/// </para>
/// <para>
/// If the row cannot be written after the host accepted the hash, the OLD hash is sent back, because a
/// host that accepts a password the panel never recorded is a mailbox its owner cannot open.
/// </para>
/// </remarks>
public sealed class ChangeMailboxPasswordCommandHandler
{
    /// <summary>Pre-compiled log delegate for a restore that did not take.</summary>
    /// <remarks>Names the address and the agent's error CODE only — never a hash, never a password.</remarks>
    private static readonly Action<ILogger, string, string, Exception?> LogRestoreFailed =
        LoggerMessage.Define<string, string>(
            LogLevel.Error,
            new EventId(1, nameof(ChangeMailboxPasswordCommandHandler)),
            "Mailbox {Address} has a new password on the mail host that the panel could not record, and the "
            + "previous one could not be put back ({AgentErrorCode}); the owner may be locked out.");

    /// <summary>The Mail module's database context, and this module's tenant boundary.</summary>
    private readonly MailDbContext _dbContext;

    /// <summary>Tells whether mail is switched on for the server.</summary>
    private readonly MailServerReader _server;

    /// <summary>The one window onto the owning account's system user name.</summary>
    private readonly IAccountDirectory _accounts;

    /// <summary>The agent, which owns everything that exists on the host.</summary>
    private readonly IAgentMailClient _agent;

    /// <summary>Hashes the owner's password the way the host verifies it.</summary>
    private readonly MailPasswordHashing _hashing;

    /// <summary>This module's audit journal.</summary>
    private readonly MailAuditJournal _journal;

    /// <summary>Where a failed restore is reported.</summary>
    private readonly ILogger<ChangeMailboxPasswordCommandHandler> _logger;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Mail module's database context.</param>
    /// <param name="server">Tells whether mail is switched on.</param>
    /// <param name="accounts">The owning account's system user name.</param>
    /// <param name="agent">The agent client that installs the hash.</param>
    /// <param name="hashing">Hashes the password for the host.</param>
    /// <param name="journal">This module's audit journal.</param>
    /// <param name="logger">Where a failed restore is reported.</param>
    public ChangeMailboxPasswordCommandHandler(
        MailDbContext dbContext,
        MailServerReader server,
        IAccountDirectory accounts,
        IAgentMailClient agent,
        MailPasswordHashing hashing,
        MailAuditJournal journal,
        ILogger<ChangeMailboxPasswordCommandHandler> logger)
    {
        _dbContext = dbContext;
        _server = server;
        _accounts = accounts;
        _agent = agent;
        _hashing = hashing;
        _journal = journal;
        _logger = logger;
    }

    /// <summary>Changes the password.</summary>
    /// <param name="command">The validated parameters; see <see cref="ChangeMailboxPasswordCommandValidator"/>.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    /// Success — or <c>MailNotEnabled</c>, <c>MailboxNotFound</c>, <c>AccountNotFound</c>,
    /// <c>MailboxProvisioningFailed</c>, the hashing failure, or the agent's own typed failure.
    /// </returns>
    public async Task<Result> HandleAsync(ChangeMailboxPasswordCommand command, CancellationToken cancellationToken)
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

        var hashed = await _hashing.HashAsync(command.Password, cancellationToken);
        if (!hashed.IsSuccess)
        {
            return await FailAsync(command, address, hashed.Error!, cancellationToken);
        }

        var installed = await _agent.SetMailboxAsync(
            MailboxAgentMapper.Current(
                account.Username, mailbox.MailDomain.Name, mailbox, new SensitiveString(hashed.Value)),
            cancellationToken);
        if (!installed.IsSuccess)
        {
            return await FailAsync(command, address, installed.Error!, cancellationToken);
        }

        var previousHash = mailbox.PasswordHash;
        mailbox.ChangePassword(hashed.Value);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            mailbox.ChangePassword(previousHash);
            await RestoreHostAsync(account, mailbox, address, cancellationToken);

            return await FailAsync(
                command,
                address,
                Error.Of(nameof(ErrorMessages.MailboxProvisioningFailed), ErrorType.Failure),
                cancellationToken);
        }

        await _journal.RecordSuccessAsync(
            HostedMailAuditActions.MailboxPasswordChanged,
            address,
            command.IpAddress,
            command.UserAgent,
            cancellationToken);

        return Result.Ok();
    }

    /// <summary>Sends the host the previous hash again, after the row refused the new one.</summary>
    /// <param name="account">The owning account, whose system user name addresses the call.</param>
    /// <param name="mailbox">The row, already holding its previous hash.</param>
    /// <param name="address">The address, for the log line.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    private async Task RestoreHostAsync(
        AccountSnapshot account,
        Mailbox mailbox,
        string address,
        CancellationToken cancellationToken)
    {
        var restored = await _agent.SetMailboxAsync(
            MailboxAgentMapper.Current(
                account.Username, mailbox.MailDomain.Name, mailbox, new SensitiveString(mailbox.PasswordHash)),
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
    private async Task<Result> FailAsync(
        ChangeMailboxPasswordCommand command,
        string subject,
        Error error,
        CancellationToken cancellationToken)
    {
        await _journal.RecordFailureAsync(
            HostedMailAuditActions.MailboxPasswordChanged,
            subject,
            command.IpAddress,
            command.UserAgent,
            cancellationToken);

        return Result.Fail(error);
    }
}
