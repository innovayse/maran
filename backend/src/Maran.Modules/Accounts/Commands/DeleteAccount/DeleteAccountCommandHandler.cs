using Maran.Agent.Client.Interfaces;
using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Accounts.Resources;
using Maran.Sdk.Contracts;
using Maran.Sdk.Events;
using Microsoft.Extensions.Logging;
using Wolverine;

namespace Maran.Modules.Accounts.Commands.DeleteAccount;

/// <summary>Handles <see cref="DeleteAccountCommand"/> by removing the account from server and panel.</summary>
public sealed class DeleteAccountCommandHandler
{
    /// <summary>
    /// Pre-compiled log delegate for a module that refused to release what it holds. Source-generated
    /// because the reason belongs to the operator and never to the customer-facing message.
    /// </summary>
    private static readonly Action<ILogger, string, Exception?> LogCleanupRefused =
        LoggerMessage.Define<string>(
            LogLevel.Error,
            new EventId(1, nameof(DeleteAccountCommandHandler)),
            "Account {AccountName} was not deleted: a module refused to release what it holds.");

    /// <summary>The Accounts module's database context.</summary>
    private readonly AccountsDbContext _dbContext;

    /// <summary>The agent, which owns the system user and the home directory.</summary>
    private readonly IAgentAccountsClient _agent;

    /// <summary>The bus the cascade is announced on.</summary>
    private readonly IMessageBus _bus;

    /// <summary>Where a subscriber's refusal is written for an operator to read.</summary>
    private readonly ILogger<DeleteAccountCommandHandler> _logger;

    /// <summary>This module's audit journal.</summary>
    private readonly AccountAuditJournal _journal;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Accounts module's database context.</param>
    /// <param name="agent">The agent client that removes the operating-system identity.</param>
    /// <param name="bus">The bus <see cref="AccountDeleting"/> is invoked on.</param>
    /// <param name="logger">Where a subscriber's refusal is recorded.</param>
    /// <param name="journal">This module's audit journal.</param>
    public DeleteAccountCommandHandler(
        AccountsDbContext dbContext,
        IAgentAccountsClient agent,
        IMessageBus bus,
        ILogger<DeleteAccountCommandHandler> logger,
        AccountAuditJournal journal)
    {
        _dbContext = dbContext;
        _agent = agent;
        _bus = bus;
        _logger = logger;
        _journal = journal;
    }

    /// <summary>Removes the account, and everything any module holds against it.</summary>
    /// <remarks>
    /// <para>
    /// <b>Three steps, and the order is the whole of the safety.</b>
    /// </para>
    /// <para>
    /// 1. <see cref="AccountDeleting"/> is INVOKED — inline, not published — so every module holding
    /// rows against this account removes them first. It is invoked rather than published because a
    /// published message is handled later, by which time the account would already be gone and a
    /// subscriber's failure could no longer stop anything. <c>userdel</c> touches neither MySQL nor
    /// sshd, so before this cascade existed a deleted account left its database rows and its SFTP
    /// login rows behind — and system user names are recycled, so an account created again under
    /// the same name inherited them.
    /// </para>
    /// <para>
    /// 2. The agent removes what is on the HOST: the databases, the SFTP logins, the jail's bind
    /// mount, the php-fpm pools, and only then the system user. It asks the machine what is there
    /// rather than being handed this panel's list, which is why step 1 running first is safe — a row
    /// this panel has already forgotten is still found and removed by name.
    /// </para>
    /// <para>
    /// 3. The <c>Account</c> row goes last. Dropping it first would be the one unrecoverable order:
    /// the panel would forget an account whose Linux user, home directory and files are still on the
    /// server, with nothing left pointing at them.
    /// </para>
    /// <para>
    /// <b>A cleanup failure aborts the deletion.</b> Either half refusing leaves the account exactly
    /// as it was, which is the recoverable state — it can be deleted again once whatever refused is
    /// fixed. The alternative, carrying on, produces an orphan: a database or a live credential
    /// nothing in the panel points at, which no later operation can find. The exception is caught
    /// rather than allowed to escape so that the operator is answered with a typed failure instead
    /// of a 500, and it is LOGGED here because the subscriber's own text has no place in a
    /// customer-facing message (rules/security.md item 8).
    /// </para>
    /// <para>
    /// <b>Where the audit entry goes follows from that.</b> The SUCCESS entry is written last, after
    /// the row is gone, because only then is every one of the three steps known to have finished —
    /// and this cascade can fail after it has already destroyed things. A deletion that dropped the
    /// account's databases and its SFTP logins and then had the agent refuse must NOT read as
    /// "AccountDeleted, succeeded": an operator searching for why a customer's data vanished would
    /// find an entry claiming a clean removal of an account that is still there. That case takes a
    /// FAILURE entry instead, on the same action and the same account name, which is the honest
    /// record of what happened — the deletion was attempted, got part-way, and did not complete.
    /// The destruction the cascade did manage is not lost from the journal either: each module
    /// journals its own removals as it makes them, so the databases and logins have their own
    /// entries under their own actions.
    /// </para>
    /// </remarks>
    /// <param name="command">Which account to remove.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>How many bytes the removal freed, or a typed failure.</returns>
    public async Task<Result<ulong>> HandleAsync(DeleteAccountCommand command, CancellationToken cancellationToken)
    {
        var account = await _dbContext.Accounts.SingleOrDefaultAsync(a => a.Id == command.AccountId, cancellationToken);
        if (account is null)
        {
            // The subject is the identifier the caller supplied, because no name is known — a probe
            // for an account the caller may not see still leaves a trace naming what was probed for.
            return await FailAsync(
                command,
                command.AccountId.ToString(),
                nameof(ErrorMessages.AccountNotFound),
                cancellationToken);
        }

        try
        {
            await _bus.InvokeAsync(new AccountDeleting(account.Id, account.Name), cancellationToken);
        }
        catch (Exception exception)
        {
            // Deliberately broad: any subscriber, including a marketplace module this assembly was
            // never compiled knowing about, may refuse in any way it likes, and every one of those
            // ways has the same meaning here — the account must NOT be deleted.
            LogCleanupRefused(_logger, account.Name, exception);

            return await FailAsync(
                command, account.Name, nameof(ErrorMessages.AccountCleanupFailed), cancellationToken);
        }

        var removed = await _agent.DeleteAsync(account.Name, cancellationToken);
        if (!removed.IsSuccess)
        {
            // The cascade above has already run, so this is the partial destruction the remarks
            // describe: journalled as a failure, never as a completed deletion.
            return await FailAsync(command, account.Name, removed.Error!.Code, cancellationToken);
        }

        // Captured before the row goes: the journal records the name, which is the only thing about
        // a deleted account anybody will later be able to search for.
        var name = account.Name;

        _dbContext.Accounts.Remove(account);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _journal.RecordSuccessAsync(
            AuditActions.AccountDeleted, name, command.IpAddress, command.UserAgent, cancellationToken);

        return Result<ulong>.Ok(removed.Value);
    }

    /// <summary>Journals a deletion that did not complete and returns it as the typed failure.</summary>
    /// <param name="command">The deletion that was refused.</param>
    /// <param name="subject">The account's name, or the supplied identifier when no row was found.</param>
    /// <param name="code">The machine-stable code to answer with.</param>
    /// <param name="cancellationToken">Cancels the journal write.</param>
    /// <returns>The failed result carrying <paramref name="code"/>.</returns>
    private async Task<Result<ulong>> FailAsync(
        DeleteAccountCommand command,
        string subject,
        string code,
        CancellationToken cancellationToken)
    {
        await _journal.RecordFailureAsync(
            AuditActions.AccountDeleted, subject, command.IpAddress, command.UserAgent, cancellationToken);

        return Result<ulong>.Fail(Error.Of(code));
    }
}
