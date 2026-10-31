using Maran.Modules.Tasks.Domain.Enums;
using Maran.Modules.Tasks.Persistence;

namespace Maran.Modules.Tasks.Queries.CountFailedTasks;

/// <summary>
/// Handles <see cref="CountFailedTasksQuery"/> with one <c>count(*)</c> over <c>tasks.PanelTasks</c>.
/// </summary>
/// <remarks>
/// Scoped by the context's query filter like every other read of the table, so a customer's count is
/// of the failed tasks they could open and an administrator's is of every failed task the retention
/// window still holds.
/// </remarks>
public sealed class CountFailedTasksQueryHandler
{
    /// <summary>The Tasks module's database context.</summary>
    private readonly TasksDbContext _dbContext;

    /// <summary>Creates the handler with the module's own database context.</summary>
    /// <param name="dbContext">The Tasks module's database context.</param>
    public CountFailedTasksQueryHandler(TasksDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>Counts the failed tasks the caller may see.</summary>
    /// <param name="query">The (parameterless) count request.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>A successful result carrying the count; this operation never fails.</returns>
    public async Task<Result<int>> HandleAsync(CountFailedTasksQuery query, CancellationToken cancellationToken)
    {
        var count = await _dbContext.PanelTasks
            .CountAsync(task => task.Status == PanelTaskStatus.Failed, cancellationToken);

        return Result<int>.Ok(count);
    }
}
