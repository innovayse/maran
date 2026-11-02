using Maran.Modules.Cron.Persistence;
using Maran.Sdk.Events;

namespace Maran.Modules.Cron.IntegrationEvents.Handlers;

/// <summary>
/// Removes a deleted account's runs started by hand, so <c>ModuleAccountResidueAuditor</c> finds nothing
/// of it left in <c>cron.CronRuns</c>.
/// </summary>
/// <remarks>
/// The account's crontab and its entry files are the agent's to remove with the account; this module's
/// own rows are the only thing it has to release.
/// </remarks>
public sealed class AccountDeletingHandler
{
    /// <summary>Where the runs are recorded.</summary>
    private readonly CronDbContext _dbContext;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">Where the runs are recorded.</param>
    public AccountDeletingHandler(CronDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>Removes every run recorded against the account being deleted.</summary>
    /// <param name="message">The account being deleted.</param>
    /// <param name="cancellationToken">Cancels the removal.</param>
    /// <returns>A task that completes once the rows are gone.</returns>
    public async Task HandleAsync(AccountDeleting message, CancellationToken cancellationToken)
    {
#pragma warning disable RS0030 // the account is being deleted, so its rows must be found whoever asked for the deletion
        var owned = await _dbContext.CronRuns
            .IgnoreQueryFilters()
            .Where(row => row.AccountId == message.AccountId)
            .ToListAsync(cancellationToken);
#pragma warning restore RS0030
        if (owned.Count == 0)
        {
            return;
        }

        _dbContext.CronRuns.RemoveRange(owned);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
