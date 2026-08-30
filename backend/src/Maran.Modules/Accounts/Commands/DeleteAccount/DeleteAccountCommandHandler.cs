using Maran.Agent.Client.Interfaces;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Accounts.Resources;

namespace Maran.Modules.Accounts.Commands.DeleteAccount;

/// <summary>Handles <see cref="DeleteAccountCommand"/> by removing the account from server and panel.</summary>
public sealed class DeleteAccountCommandHandler
{
    /// <summary>The Accounts module's database context.</summary>
    private readonly AccountsDbContext _dbContext;

    /// <summary>The agent, which owns the system user and the home directory.</summary>
    private readonly IAgentAccountsClient _agent;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Accounts module's database context.</param>
    /// <param name="agent">The agent client that removes the operating-system identity.</param>
    public DeleteAccountCommandHandler(AccountsDbContext dbContext, IAgentAccountsClient agent)
    {
        _dbContext = dbContext;
        _agent = agent;
    }

    /// <summary>Removes the account.</summary>
    /// <remarks>
    /// The agent goes first and the row is dropped only if it succeeded. Dropping the row first
    /// would be the one unrecoverable order: the panel would forget an account whose Linux user,
    /// home directory and files are still on the server, with nothing left pointing at them.
    ///
    /// Databases and FTP users are not removed here. The agent's contract is explicit that its
    /// account deletion does not touch them, so they are dropped through their own services first
    /// — which is also what makes each removal separately auditable.
    /// </remarks>
    /// <param name="command">Which account to remove.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>How many bytes the removal freed, or a typed failure.</returns>
    public async Task<Result<ulong>> HandleAsync(DeleteAccountCommand command, CancellationToken cancellationToken)
    {
        var account = await _dbContext.Accounts.SingleOrDefaultAsync(a => a.Id == command.AccountId, cancellationToken);
        if (account is null)
        {
            return Result<ulong>.Fail(Error.Of(nameof(ErrorMessages.AccountNotFound)));
        }

        var removed = await _agent.DeleteAsync(account.Name, cancellationToken);
        if (!removed.IsSuccess)
        {
            return Result<ulong>.Fail(removed.Error!);
        }

        _dbContext.Accounts.Remove(account);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result<ulong>.Ok(removed.Value);
    }
}
