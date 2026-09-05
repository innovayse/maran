using Maran.Agent.Client.Interfaces;
using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Accounts.Resources;
using Maran.Modules.Accounts.Services;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Accounts.Commands.SuspendAccount;

/// <summary>Handles <see cref="SuspendAccountCommand"/> by stopping the account on the server.</summary>
public sealed class SuspendAccountCommandHandler
{
    /// <summary>The Accounts module's database context.</summary>
    private readonly AccountsDbContext _dbContext;

    /// <summary>The agent, which owns everything outside the database.</summary>
    private readonly IAgentAccountsClient _agent;

    /// <summary>This module's audit journal.</summary>
    private readonly AccountAuditJournal _journal;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Accounts module's database context.</param>
    /// <param name="agent">The agent client that performs the privileged half.</param>
    /// <param name="journal">This module's audit journal.</param>
    public SuspendAccountCommandHandler(
        AccountsDbContext dbContext,
        IAgentAccountsClient agent,
        AccountAuditJournal journal)
    {
        _dbContext = dbContext;
        _agent = agent;
        _journal = journal;
    }

    /// <summary>Suspends the account. Idempotent: suspending a suspended account changes nothing.</summary>
    /// <remarks>
    /// The agent runs first and the row is written only if it succeeded. The two can still
    /// disagree if the database write fails afterwards, and the order decides WHICH way: the
    /// account is really stopped while the panel still shows it active. That is the safe
    /// direction — the panel understating what it has done is a wrong label, where the reverse
    /// is a customer told their account is suspended while their sites keep serving.
    /// </remarks>
    /// <param name="command">Which account to suspend.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>The account in its new state, or a typed failure.</returns>
    public async Task<Result<AccountDto>> HandleAsync(SuspendAccountCommand command, CancellationToken cancellationToken)
    {
        var account = await _dbContext.Accounts.SingleOrDefaultAsync(a => a.Id == command.AccountId, cancellationToken);
        if (account is null)
        {
            // The subject is the identifier the caller supplied, because no name is known — a probe
            // for an account the caller may not see still leaves a trace naming what was probed for.
            return await FailAsync(
                command,
                command.AccountId.ToString(),
                Error.Of(nameof(ErrorMessages.AccountNotFound), ErrorType.NotFound),
                cancellationToken);
        }

        var stopped = await _agent.SuspendAsync(account.Name, cancellationToken);
        if (!stopped.IsSuccess)
        {
            return await FailAsync(command, account.Name, stopped.Error!, cancellationToken);
        }

        account.Suspend();
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _journal.RecordSuccessAsync(
            AuditActions.AccountSuspended, account.Name, command.IpAddress, command.UserAgent, cancellationToken);

        return Result<AccountDto>.Ok(new AccountDto(
            account.Id, account.Name, account.PrimaryDomain, account.PlanId, account.Status, account.CreatedAt));
    }

    /// <summary>Journals a refused suspend and returns it as the typed failure.</summary>
    /// <param name="command">The suspend that was refused.</param>
    /// <param name="subject">The account's name, or the supplied identifier when no row was found.</param>
    /// <param name="error">The typed failure to answer with, code and kind together.</param>
    /// <param name="cancellationToken">Cancels the journal write.</param>
    /// <returns>The failed result carrying <paramref name="error"/>.</returns>
    private async Task<Result<AccountDto>> FailAsync(
        SuspendAccountCommand command,
        string subject,
        Error error,
        CancellationToken cancellationToken)
    {
        await _journal.RecordFailureAsync(
            AuditActions.AccountSuspended, subject, command.IpAddress, command.UserAgent, cancellationToken);

        return Result<AccountDto>.Fail(error);
    }
}
