using Maran.Modules.ServerMigration.Domain.Entities;
using Maran.Modules.ServerMigration.Models;
using Maran.Modules.ServerMigration.Resources;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.ServerMigration.Services;

/// <summary>
/// Ends one account of a job at the administrator's request and reclaims what it created (issue #197, phase 2).
/// </summary>
/// <remarks>
/// A running account is stopped through the registry and ends itself (its worker deletes the account and records the
/// end). An account with no worker is ended here, on the administrator's own request and caller: the account it
/// created is deleted through the import rollback, which refuses an account older than the account's first run, and a
/// rollback that cannot finish leaves the row naming the account instead of claiming it is gone.
/// </remarks>
public sealed class MigrationAccountCanceller
{
    /// <summary>What this process runs.</summary>
    private readonly MigrationRunRegistry _registry;

    /// <summary>Deletes the account the run created.</summary>
    private readonly IAccountMigrationRollback? _rollback;

    /// <summary>The panel's clock.</summary>
    private readonly IClock _clock;

    /// <summary>Creates the canceller.</summary>
    /// <param name="registry">What this process runs.</param>
    /// <param name="rollbacks">Deletes the account the run created; a collection, because an absent module registers nothing.</param>
    /// <param name="clock">The panel's clock.</param>
    public MigrationAccountCanceller(
        MigrationRunRegistry registry,
        IEnumerable<IAccountMigrationRollback> rollbacks,
        IClock clock)
    {
        _registry = registry;
        _rollback = rollbacks.FirstOrDefault();
        _clock = clock;
    }

    /// <summary>Cancels an account that is not completed. The caller saves.</summary>
    /// <param name="account">The row.</param>
    /// <param name="cancellationToken">Cancels the deletion.</param>
    /// <returns>Success, or why the account remains; a stopped worker is a success, its end is the worker's.</returns>
    public async Task<Result> CancelAsync(MigrationJobAccount account, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(account);

        if (_registry.IsRunning(account.Id))
        {
            _registry.Stop(account.Id, MigrationStopReason.Cancel);

            return Result.Ok();
        }

        if (account.TargetAccountId is { } target)
        {
            var deleted = _rollback is null
                ? Result.Fail(Error.Of(nameof(ErrorMessages.MigrationRollbackUnavailable), ErrorType.Conflict))
                : await _rollback.DeleteImportedAccountAsync(target, account.StartedAt ?? _clock.UtcNow, cancellationToken);
            if (!deleted.IsSuccess)
            {
                account.RollbackFailed(nameof(ErrorMessages.MigrationAccountCancelled), _clock.UtcNow);

                return deleted;
            }
        }

        account.Cancelled(_clock.UtcNow);

        return Result.Ok();
    }
}
