using Maran.Agent.Client.Interfaces;
using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Accounts.Resources;

namespace Maran.Modules.Accounts.Commands.ReactivateAccount;

/// <summary>Handles <see cref="ReactivateAccountCommand"/> by restarting the account on the server.</summary>
public sealed class ReactivateAccountCommandHandler
{
    /// <summary>The Accounts module's database context.</summary>
    private readonly AccountsDbContext _dbContext;

    /// <summary>The agent, which owns everything outside the database.</summary>
    private readonly IAgentAccountsClient _agent;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Accounts module's database context.</param>
    /// <param name="agent">The agent client that performs the privileged half.</param>
    public ReactivateAccountCommandHandler(AccountsDbContext dbContext, IAgentAccountsClient agent)
    {
        _dbContext = dbContext;
        _agent = agent;
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
            return Result<AccountDto>.Fail(Error.Of(nameof(ErrorMessages.AccountNotFound)));
        }

        var started = await _agent.UnsuspendAsync(account.Name, cancellationToken);
        if (!started.IsSuccess)
        {
            return Result<AccountDto>.Fail(started.Error!);
        }

        account.Reactivate();
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result<AccountDto>.Ok(new AccountDto(
            account.Id, account.Name, account.PrimaryDomain, account.PlanId, account.Status, account.CreatedAt));
    }
}
