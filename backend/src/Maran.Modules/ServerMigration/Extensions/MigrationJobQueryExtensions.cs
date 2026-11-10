using Maran.Modules.ServerMigration.Domain.Entities;
using Maran.Modules.ServerMigration.Domain.Enums;
using Maran.Modules.ServerMigration.Persistence;

namespace Maran.Modules.ServerMigration.Extensions;

/// <summary>Reads shared by the job operations (issue #197, phase 2).</summary>
public static class MigrationJobQueryExtensions
{
    /// <summary>Counts a job's accounts in each state.</summary>
    /// <param name="dbContext">The module's database.</param>
    /// <param name="jobId">The job.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>A count per state that has any account.</returns>
    public static async Task<IReadOnlyDictionary<MigrationAccountState, int>> CountStatesAsync(
        this ServerMigrationDbContext dbContext,
        Guid jobId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        var rows = await dbContext.MigrationJobAccounts.AsNoTracking()
            .Where(account => account.JobId == jobId)
            .GroupBy(account => account.State)
            .Select(group => new { State = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(row =>
        {
            return row.State;
        }, row =>
        {
            return row.Count;
        });
    }

    /// <summary>Finds a job's account row, tracked.</summary>
    /// <param name="dbContext">The module's database.</param>
    /// <param name="jobId">The job.</param>
    /// <param name="accountId">The row.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The row, or <c>null</c> when the job has no such account.</returns>
    public static async Task<MigrationJobAccount?> FindAccountAsync(
        this ServerMigrationDbContext dbContext,
        Guid jobId,
        Guid accountId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        return await dbContext.MigrationJobAccounts.FirstOrDefaultAsync(
            account => account.Id == accountId && account.JobId == jobId, cancellationToken);
    }
}
