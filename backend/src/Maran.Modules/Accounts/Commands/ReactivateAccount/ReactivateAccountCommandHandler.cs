using Maran.Agent.Client.Interfaces;
using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Accounts.Resources;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Accounts.Commands.ReactivateAccount;

/// <summary>Handles <see cref="ReactivateAccountCommand"/> by restarting the account on the server.</summary>
public sealed class ReactivateAccountCommandHandler
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
    public ReactivateAccountCommandHandler(
        AccountsDbContext dbContext,
        IAgentAccountsClient agent,
        AccountAuditJournal journal)
    {
        _dbContext = dbContext;
        _agent = agent;
        _journal = journal;
    }

    /// <summary>Reactivates the account. Idempotent: reactivating an active account changes nothing.</summary>
    /// <remarks>
    /// The agent runs first, as in suspension, and the same ordering keeps the failure on the
    /// understating side: if the row write fails the account is serving again while the panel
    /// still says suspended, rather than the panel promising service that is not running.
    /// </remarks>
    /// <param name="command">Which account to reactivate.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>The account in its new state, or a typed failure.</returns>
    public async Task<Result<AccountDto>> HandleAsync(
        ReactivateAccountCommand command,
        CancellationToken cancellationToken)
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

        var started = await _agent.UnsuspendAsync(account.Name, cancellationToken);
        if (!started.IsSuccess)
        {
            return await FailAsync(command, account.Name, started.Error!.Code, cancellationToken);
        }

        account.Reactivate();
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _journal.RecordSuccessAsync(
            AuditActions.AccountReactivated, account.Name, command.IpAddress, command.UserAgent, cancellationToken);

        return Result<AccountDto>.Ok(new AccountDto(
            account.Id, account.Name, account.PrimaryDomain, account.PlanId, account.Status, account.CreatedAt));
    }

    /// <summary>Journals a refused reactivate and returns it as the typed failure.</summary>
    /// <param name="command">The reactivate that was refused.</param>
    /// <param name="subject">The account's name, or the supplied identifier when no row was found.</param>
    /// <param name="code">The machine-stable code to answer with.</param>
    /// <param name="cancellationToken">Cancels the journal write.</param>
    /// <returns>The failed result carrying <paramref name="code"/>.</returns>
    private async Task<Result<AccountDto>> FailAsync(
        ReactivateAccountCommand command,
        string subject,
        string code,
        CancellationToken cancellationToken)
    {
        await _journal.RecordFailureAsync(
            AuditActions.AccountReactivated, subject, command.IpAddress, command.UserAgent, cancellationToken);

        return Result<AccountDto>.Fail(Error.Of(code));
    }
}
