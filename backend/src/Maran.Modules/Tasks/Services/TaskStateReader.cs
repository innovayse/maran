using Maran.Modules.Tasks.Domain.Enums;
using Maran.Modules.Tasks.Persistence;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Tasks.Services;

/// <summary>
/// The panel's <see cref="ITaskStateReader"/>: answers whether a task is still running from the task's own
/// row, and nothing else about it.
/// </summary>
/// <remarks>
/// <b>It reads with <c>IgnoreQueryFilters</c>, and must.</b> The module's query filter admits
/// administrators and a task's own account, and the caller is a pass with no signed-in account of the
/// task's: the filtered read would find nothing and answer <see cref="TaskState.Unknown"/> for a task that
/// is running, which is the answer that lets a caller settle work in flight. It is safe because only the
/// state leaves this class.
/// </remarks>
public sealed class TaskStateReader : ITaskStateReader
{
    /// <summary>The Tasks module's database context.</summary>
    private readonly TasksDbContext _dbContext;

    /// <summary>Creates the reader.</summary>
    /// <param name="dbContext">The Tasks module's database context.</param>
    public TaskStateReader(TasksDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <inheritdoc />
    public async Task<TaskState> ReadAsync(Guid taskId, CancellationToken cancellationToken)
    {
#pragma warning disable RS0030 // the caller is a pass with no signed-in account of the task's; only the state leaves this class
        var status = await _dbContext.PanelTasks
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(task => task.Id == taskId)
            .Select(task => (PanelTaskStatus?)task.Status)
            .SingleOrDefaultAsync(cancellationToken);
#pragma warning restore RS0030

        return status switch
        {
            PanelTaskStatus.Running => TaskState.Running,
            PanelTaskStatus.Completed => TaskState.Completed,
            PanelTaskStatus.Failed => TaskState.Failed,
            _ => TaskState.Unknown,
        };
    }
}
