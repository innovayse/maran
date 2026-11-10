using Maran.Modules.Accounts.Commands.DeleteAccount;
using Maran.Modules.Accounts.Persistence;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Wolverine;

namespace Maran.Modules.Accounts.Services;

/// <summary>
/// Deletes the account a failed import created, through the ordinary deletion, and refuses any other (issue
/// #127). The Accounts module's <see cref="IAccountMigrationRollback"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>The refusal is the point.</b> An account whose creation time precedes the import's start was not made by
/// it, whatever id the caller names: the rollback is answered as not found, the same answer a missing account
/// gets, so it cannot be used to probe which accounts exist.
/// </para>
/// <para>
/// The deletion is <see cref="DeleteAccountCommand"/> with the final backup skipped, so the cascade, the
/// residue audit and the journal are the ordinary ones. It runs under the administrator the job acts as.
/// </para>
/// </remarks>
public sealed class AccountMigrationRollback : IAccountMigrationRollback
{
    /// <summary>The module's database context.</summary>
    private readonly AccountsDbContext _dbContext;

    /// <summary>The bus the deletion is dispatched on.</summary>
    private readonly IMessageBus _bus;

    /// <summary>Creates the rollback.</summary>
    /// <param name="dbContext">The module's database context.</param>
    /// <param name="bus">The bus the deletion is dispatched on.</param>
    public AccountMigrationRollback(AccountsDbContext dbContext, IMessageBus bus)
    {
        _dbContext = dbContext;
        _bus = bus;
    }

    /// <inheritdoc />
    public async Task<Result> DeleteImportedAccountAsync(
        Guid accountId,
        DateTimeOffset importStartedAt,
        CancellationToken cancellationToken)
    {
        var account = await _dbContext.Accounts
            .AsNoTracking()
            .FirstOrDefaultAsync(row => row.Id == accountId, cancellationToken);

        if (account is null || account.CreatedAt < importStartedAt)
        {
            return Result.Fail(Error.Of(MigrationCodes.RollbackRefused, ErrorType.NotFound));
        }

        var deleted = await _bus.InvokeAsync<Result<ulong>>(
            new DeleteAccountCommand(accountId, string.Empty, string.Empty, SkipFinalBackup: true),
            cancellationToken);

        return deleted.IsSuccess ? Result.Ok() : Result.Fail(deleted.Error!);
    }
}
