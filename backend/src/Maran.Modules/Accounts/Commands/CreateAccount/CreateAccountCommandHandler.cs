using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Domain;
using Maran.Modules.Accounts.Resources;
using Maran.Modules.Accounts.Persistence;

namespace Maran.Modules.Accounts.Commands.CreateAccount;

/// <summary>
/// Handles <see cref="CreateAccountCommand"/> by inserting the account row into
/// <c>accounts.Accounts</c>. Row creation only — provisioning the account's system user comes with
/// the agent's Accounts operations, which do not exist yet (spec §8).
/// </summary>
public sealed class CreateAccountCommandHandler
{
    /// <summary>The Accounts module's database context.</summary>
    private readonly AccountsDbContext _dbContext;

    /// <summary>The injected time source; never the ambient clock (rules/csharp.md).</summary>
    private readonly IClock _clock;

    /// <summary>Creates the handler with the module's own database context and the panel's clock.</summary>
    /// <param name="dbContext">The Accounts module's database context.</param>
    /// <param name="clock">The injected time source used to stamp the new account's creation time.</param>
    public CreateAccountCommandHandler(AccountsDbContext dbContext, IClock clock)
    {
        _dbContext = dbContext;
        _clock = clock;
    }

    /// <summary>
    /// Creates the account row, guarding against a duplicate name or domain.
    /// </summary>
    /// <param name="command">The validated account parameters; see <see cref="CreateAccountCommandValidator"/>.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The created account, or <c>AccountNameTaken</c>/<c>AccountDomainTaken</c>.</returns>
    public async Task<Result<AccountDto>> Handle(CreateAccountCommand command, CancellationToken cancellationToken)
    {
        var nameTaken = await _dbContext.Accounts
            .AsNoTracking()
            .AnyAsync(a => a.Name == command.Name, cancellationToken);
        if (nameTaken)
        {
            return Result<AccountDto>.Fail(Error.Of(nameof(ErrorMessages.AccountNameTaken)));
        }

        var domainTaken = await _dbContext.Accounts
            .AsNoTracking()
            .AnyAsync(a => a.PrimaryDomain == command.PrimaryDomain, cancellationToken);
        if (domainTaken)
        {
            return Result<AccountDto>.Fail(Error.Of(nameof(ErrorMessages.AccountDomainTaken)));
        }

        var account = new Account(Guid.NewGuid(), command.Name, command.PrimaryDomain, command.PlanId, _clock.UtcNow);

        _dbContext.Accounts.Add(account);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result<AccountDto>.Ok(
            new AccountDto(account.Id, account.Name, account.PrimaryDomain, account.PlanId, account.Status, account.CreatedAt));
    }
}
