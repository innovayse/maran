using Maran.Agent.Client.Interfaces;
using Maran.Modules.Mail.Common;
using Maran.Modules.Mail.Domain.Entities;
using Maran.Modules.Mail.Domain.Enums;
using Maran.Modules.Mail.Domain.Policies;
using Maran.Modules.Mail.Interfaces;
using Maran.Modules.Mail.Mappers;
using Maran.Modules.Mail.Persistence;
using Maran.Modules.Mail.Resources;
using Maran.Modules.Mail.Services;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Maran.SharedKernel.Security;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Maran.Modules.Mail.Commands.CreateMailbox;

/// <summary>
/// Handles <see cref="CreateMailboxCommand"/>: refuses what the plan or the domain's own rows do not
/// allow, provisions the mailbox through the agent, and only then records the row that owns it —
/// compensating on the host if that record cannot be written.
/// </summary>
/// <remarks>
/// <para>
/// <b>Order: plan first, agent second, row third.</b> The mailbox allowance and the quota pool are
/// checked before the agent is called, so a mailbox the plan refuses never reaches the host. The agent
/// runs before the row because a mailbox with no row is invisible and, with the compensation below,
/// short-lived, while a row with no mailbox is a customer told they have an address whose login the
/// host refuses.
/// </para>
/// <para>
/// <b>The limits are checked twice, and the SECOND check is the one that holds.</b> The first is
/// count-then-insert: two concurrent creations both read N-1 and both pass. It is kept because it
/// refuses the ordinary over-limit request before the host is touched. <see cref="IMailboxSlotGate"/>
/// writes the row under a per-account advisory lock with the count and the pool re-taken inside it, so
/// the second of two simultaneous creations is refused there, and its host-side mailbox is removed again
/// (<c>deleteData: true</c> — it was created a moment ago and holds nothing).
/// </para>
/// <para>
/// <b>The duplicate branch is narrowed to SqlState 23505 and to nothing else.</b> A 23505 means a
/// concurrent creation of the same address won and ITS row owns the host's mailbox, so removing it would
/// destroy a mailbox another request has just been told exists; every other database failure leaves
/// nothing owning the host's mailbox and falls through to the compensating branch.
/// </para>
/// <para>
/// <b>One address space per domain.</b> A mailbox and an alias may not share a local part (mail to it
/// would have two meanings), and no index across two tables can state that, so both tables are asked
/// here; the unique index on the mailbox table closes only the mailbox-against-mailbox race.
/// </para>
/// <para>
/// The password is hashed here and the plaintext goes no further: the agent receives the hash, the row
/// stores the hash, and neither the journal nor the answer nor a log line carries the password.
/// </para>
/// </remarks>
public sealed class CreateMailboxCommandHandler
{
    /// <summary>Pre-compiled log delegate for a compensation that did not take.</summary>
    /// <remarks>
    /// Names the address and the agent's own error CODE, never the agent's text and never the password.
    /// What it reports is a mailbox on the host that only an operator can clear.
    /// </remarks>
    private static readonly Action<ILogger, string, string, Exception?> LogCompensationFailed =
        LoggerMessage.Define<string, string>(
            LogLevel.Error,
            new EventId(1, nameof(CreateMailboxCommandHandler)),
            "Created mailbox {Address} could not be recorded and could not be removed from the mail host "
            + "either ({AgentErrorCode}); it is now on the host with no row.");

    /// <summary>The Mail module's database context, and this module's tenant boundary.</summary>
    private readonly MailDbContext _dbContext;

    /// <summary>Tells whether mail is switched on for the server.</summary>
    private readonly MailServerReader _server;

    /// <summary>The one window onto the owning account's system user name and plan allowances.</summary>
    private readonly IAccountDirectory _accounts;

    /// <summary>The agent, which owns everything that exists on the host.</summary>
    private readonly IAgentMailClient _agent;

    /// <summary>Hashes the owner's password the way the host verifies it.</summary>
    private readonly MailPasswordHashing _hashing;

    /// <summary>The atomic claim on the account's next mailbox slot and quota.</summary>
    private readonly IMailboxSlotGate _slotGate;

    /// <summary>This module's audit journal.</summary>
    private readonly MailAuditJournal _journal;

    /// <summary>The injected time source; never the ambient clock (rules/csharp.md).</summary>
    private readonly IClock _clock;

    /// <summary>The module's display names, which name the new mailbox's status.</summary>
    private readonly IStringLocalizer<DisplayNames> _names;

    /// <summary>Where a failed compensation is reported, since the customer is told nothing about it.</summary>
    private readonly ILogger<CreateMailboxCommandHandler> _logger;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Mail module's database context.</param>
    /// <param name="server">Tells whether mail is switched on.</param>
    /// <param name="accounts">The owning account's system user name and plan allowances.</param>
    /// <param name="agent">The agent client that provisions the mailbox.</param>
    /// <param name="hashing">Hashes the password for the host.</param>
    /// <param name="slotGate">Takes the account's slot atomically when the row is written.</param>
    /// <param name="journal">This module's audit journal.</param>
    /// <param name="clock">The injected time source used to stamp the new row.</param>
    /// <param name="names">The module's display names.</param>
    /// <param name="logger">Where a failed compensation is reported.</param>
    public CreateMailboxCommandHandler(
        MailDbContext dbContext,
        MailServerReader server,
        IAccountDirectory accounts,
        IAgentMailClient agent,
        MailPasswordHashing hashing,
        IMailboxSlotGate slotGate,
        MailAuditJournal journal,
        IClock clock,
        IStringLocalizer<DisplayNames> names,
        ILogger<CreateMailboxCommandHandler> logger)
    {
        _dbContext = dbContext;
        _server = server;
        _accounts = accounts;
        _agent = agent;
        _hashing = hashing;
        _slotGate = slotGate;
        _journal = journal;
        _clock = clock;
        _names = names;
        _logger = logger;
    }

    /// <summary>Creates the mailbox, refusing it before the host is touched when anything says no.</summary>
    /// <param name="command">The validated parameters; see <see cref="CreateMailboxCommandValidator"/>.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    /// The new mailbox — or <c>MailNotEnabled</c>, <c>MailDomainNotFound</c>, <c>AccountNotFound</c>,
    /// <c>MailboxLimitReached</c>, <c>MailQuotaPoolExhausted</c>, <c>MailboxTaken</c>,
    /// <c>MailboxLimitReachedConcurrently</c>, <c>MailboxProvisioningFailed</c>, the hashing failure, or the
    /// agent's own typed failure.
    /// </returns>
    public async Task<Result<MailboxDto>> HandleAsync(
        CreateMailboxCommand command,
        CancellationToken cancellationToken)
    {
        var localPart = MailboxAddressPolicy.Normalize(command.LocalPart);

        if (await _server.FindEnabledAsync(cancellationToken) is null)
        {
            return await FailAsync(
                command,
                command.MailDomainId.ToString(),
                Error.Of(nameof(ErrorMessages.MailNotEnabled), ErrorType.Conflict),
                cancellationToken);
        }

        // Tenant-scoped: another account's domain is absent from this context, so a guessed id reads as
        // "not found" rather than "forbidden".
        var domain = await _dbContext.MailDomains
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == command.MailDomainId, cancellationToken);
        if (domain is null)
        {
            return await FailAsync(
                command,
                command.MailDomainId.ToString(),
                Error.Of(nameof(ErrorMessages.MailDomainNotFound), ErrorType.NotFound),
                cancellationToken);
        }

        var address = MailboxAddressPolicy.Compose(localPart, domain.Name);

        var account = await _accounts.FindAsync(domain.AccountId, cancellationToken);
        if (account is null)
        {
            return await FailAsync(
                command,
                address,
                Error.Of(nameof(ErrorMessages.AccountNotFound), ErrorType.NotFound),
                cancellationToken);
        }

        var limits = account.MailLimits;
        var held = await _dbContext.Mailboxes
            .AsNoTracking()
            .Where(existing => existing.AccountId == domain.AccountId)
            .Select(existing => existing.QuotaMib)
            .ToListAsync(cancellationToken);
        if (held.Count >= limits.MaxMailboxes)
        {
            return await FailAsync(
                command,
                address,
                Error.Of(nameof(ErrorMessages.MailboxLimitReached), ErrorType.Conflict),
                cancellationToken);
        }

        if (held.Sum(quota =>
        {
            return (long)quota;
        }) + command.QuotaMib > limits.MailboxQuotaPoolMib)
        {
            return await FailAsync(
                command,
                address,
                Error.Of(nameof(ErrorMessages.MailQuotaPoolExhausted), ErrorType.Conflict),
                cancellationToken);
        }

        var taken = await _dbContext.Mailboxes.AsNoTracking().AnyAsync(
                existing => existing.MailDomainId == domain.Id && existing.LocalPart == localPart,
                cancellationToken)
            || await _dbContext.MailAliases.AsNoTracking().AnyAsync(
                existing => existing.MailDomainId == domain.Id && existing.LocalPart == localPart,
                cancellationToken);
        if (taken)
        {
            return await FailAsync(
                command,
                address,
                Error.Of(nameof(ErrorMessages.MailboxTaken), ErrorType.Conflict),
                cancellationToken);
        }

        var hashed = await _hashing.HashAsync(command.Password, cancellationToken);
        if (!hashed.IsSuccess)
        {
            return await FailAsync(command, address, hashed.Error!, cancellationToken);
        }

        var mailbox = new Mailbox(
            Guid.NewGuid(),
            domain.AccountId,
            domain.Id,
            localPart,
            hashed.Value,
            command.QuotaMib,
            limits.MailboxSendLimitPerHour,
            _clock.UtcNow);

        var provisioned = await _agent.SetMailboxAsync(
            MailboxAgentMapper.Current(account.Username, domain.Name, mailbox, new SensitiveString(hashed.Value)),
            cancellationToken);
        if (!provisioned.IsSuccess)
        {
            return await FailAsync(command, address, provisioned.Error!, cancellationToken);
        }

        var recorded = await RecordAsync(mailbox, account, domain, address, command, cancellationToken);
        if (!recorded.IsSuccess)
        {
            return Result<MailboxDto>.Fail(recorded.Error!);
        }

        await _journal.RecordSuccessAsync(
            HostedMailAuditActions.MailboxCreated, address, command.IpAddress, command.UserAgent, cancellationToken);

        var ownerNames = await _accounts.NamesOfAsync([mailbox.AccountId], cancellationToken);
        var status = MailAccountStates.MailboxStatusOf(mailbox.Enabled, account.State == AccountState.Suspended);

        return Result<MailboxDto>.Ok(MailboxMapper.From(mailbox, domain.Name, ownerNames, null, status, _names));
    }

    /// <summary>Writes the row for a mailbox the agent has already made, compensating if it cannot.</summary>
    /// <param name="mailbox">The row to write.</param>
    /// <param name="account">The owning account, whose plan decides the slot and addresses the compensation.</param>
    /// <param name="domain">The domain the mailbox lives in.</param>
    /// <param name="address">The full address, for the journal and the compensating call.</param>
    /// <param name="command">The creation being recorded.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>Success, or the typed failure the caller is answered with.</returns>
    private async Task<Result> RecordAsync(
        Mailbox mailbox,
        AccountSnapshot account,
        MailDomain domain,
        string address,
        CreateMailboxCommand command,
        CancellationToken cancellationToken)
    {
        try
        {
            var outcome = await _slotGate.TryTakeAsync(
                mailbox, account.MailLimits.MaxMailboxes, account.MailLimits.MailboxQuotaPoolMib, cancellationToken);
            if (outcome == MailboxSlotOutcome.Taken)
            {
                return Result.Ok();
            }

            // The gate refused, which means another creation took the slot or the pool while this one was
            // on the host. Nothing owns the mailbox this request made, so it goes, and the refusal says
            // the plan filled up rather than that the name was taken.
            await CompensateAsync(account, domain, mailbox, address, cancellationToken);

            var refusal = outcome == MailboxSlotOutcome.QuotaPoolExhausted
                ? Error.Of(nameof(ErrorMessages.MailQuotaPoolExhausted), ErrorType.Conflict)
                : Error.Of(nameof(ErrorMessages.MailboxLimitReachedConcurrently), ErrorType.Conflict);

            return Result.Fail(await FailedErrorAsync(command, address, refusal, cancellationToken));
        }
        catch (DbUpdateException exception)
        {
            _dbContext.Mailboxes.Remove(mailbox);

            // The ONE narrowing, and everything below turns on it: SqlState 23505 and nothing else means
            // a duplicate. A concurrent creation of this address won, ITS row owns the host's mailbox, and
            // removing it would delete mail the winner has already been told they can receive.
            if (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                return Result.Fail(await FailedErrorAsync(
                    command,
                    address,
                    Error.Of(nameof(ErrorMessages.MailboxTaken), ErrorType.Conflict),
                    cancellationToken));
            }

            await CompensateAsync(account, domain, mailbox, address, cancellationToken);

            return Result.Fail(await FailedErrorAsync(
                command,
                address,
                Error.Of(nameof(ErrorMessages.MailboxProvisioningFailed), ErrorType.Failure),
                cancellationToken));
        }
    }

    /// <summary>Removes a mailbox the agent made but no row owns.</summary>
    /// <param name="account">The owning account, whose system user name addresses the call.</param>
    /// <param name="domain">The domain the mailbox lives in.</param>
    /// <param name="mailbox">The mailbox that was not recorded.</param>
    /// <param name="address">The address, for the log line.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <remarks>
    /// Best effort, logged rather than surfaced: the caller is already being told the creation failed. The
    /// data is deleted with the mailbox because the mailbox was created a moment ago in this request and
    /// nothing else can have written to it.
    /// </remarks>
    private async Task CompensateAsync(
        AccountSnapshot account,
        MailDomain domain,
        Mailbox mailbox,
        string address,
        CancellationToken cancellationToken)
    {
        var removed = await _agent.RemoveMailboxAsync(
            account.Username, domain.Name, mailbox.LocalPart, deleteData: true, cancellationToken);
        if (!removed.IsSuccess)
        {
            LogCompensationFailed(_logger, address, removed.Error!.Code, null);
        }
    }

    /// <summary>Journals a refused creation and hands back the code to answer with.</summary>
    /// <param name="command">The creation that was refused.</param>
    /// <param name="subject">The address, or the domain identifier when no address could be formed.</param>
    /// <param name="error">The typed failure to answer with, code and kind together.</param>
    /// <param name="cancellationToken">Cancels the journal write.</param>
    /// <returns><paramref name="error"/>, unchanged.</returns>
    private async Task<Error> FailedErrorAsync(
        CreateMailboxCommand command,
        string subject,
        Error error,
        CancellationToken cancellationToken)
    {
        await _journal.RecordFailureAsync(
            HostedMailAuditActions.MailboxCreated, subject, command.IpAddress, command.UserAgent, cancellationToken);

        return error;
    }

    /// <summary>Journals a refused creation and returns it as the typed failure.</summary>
    /// <param name="command">The creation that was refused.</param>
    /// <param name="subject">The address, or the domain identifier when no address could be formed.</param>
    /// <param name="error">The typed failure to answer with, code and kind together.</param>
    /// <param name="cancellationToken">Cancels the journal write.</param>
    /// <returns>The failed result carrying <paramref name="error"/>.</returns>
    private async Task<Result<MailboxDto>> FailAsync(
        CreateMailboxCommand command,
        string subject,
        Error error,
        CancellationToken cancellationToken)
    {
        return Result<MailboxDto>.Fail(await FailedErrorAsync(command, subject, error, cancellationToken));
    }
}
