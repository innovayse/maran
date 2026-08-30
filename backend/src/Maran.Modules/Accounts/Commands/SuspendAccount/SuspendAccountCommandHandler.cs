using Maran.Agent.Client.Interfaces;
using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Accounts.Resources;

namespace Maran.Modules.Accounts.Commands.SuspendAccount;

/// <summary>Handles <see cref="SuspendAccountCommand"/> by stopping the account on the server.</summary>
public sealed class SuspendAccountCommandHandler
{
    /// <summary>The Accounts module's database context.</summary>
    private readonly AccountsDbContext _dbContext;

    /// <summary>The agent, which owns everything outside the database.</summary>
    private readonly IAgentAccountsClient _agent;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Accounts module's database context.</param>
    /// <param name="agent">The agent client that performs the privileged half.</param>
    public SuspendAccountCommandHandler(AccountsDbContext dbContext, IAgentAccountsClient agent)
    {
        _dbContext = dbContext;
        _agent = agent;
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
            return Result<AccountDto>.Fail(Error.Of(nameof(ErrorMessages.AccountNotFound)));
        }

        var stopped = await _agent.SuspendAsync(account.Name, cancellationToken);
        if (!stopped.IsSuccess)
        {
            return Result<AccountDto>.Fail(stopped.Error!);
        }

        account.Suspend();
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result<AccountDto>.Ok(new AccountDto(
            account.Id, account.Name, account.PrimaryDomain, account.PlanId, account.Status, account.CreatedAt));
    }
}
