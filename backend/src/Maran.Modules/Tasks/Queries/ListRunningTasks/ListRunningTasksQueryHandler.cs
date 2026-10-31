using Maran.Modules.Tasks.Common;
using Maran.Modules.Tasks.Domain.Enums;
using Maran.Modules.Tasks.Mappers;
using Maran.Modules.Tasks.Persistence;
using Maran.Modules.Tasks.Services;

namespace Maran.Modules.Tasks.Queries.ListRunningTasks;

/// <summary>
/// Handles <see cref="ListRunningTasksQuery"/> by reading the running rows of <c>tasks.PanelTasks</c>
/// the caller may see.
/// </summary>
/// <remarks>
/// Visibility is the context's query filter, exactly as for the paged list; the log goes to an
/// administrator only (<see cref="PanelTaskMapper"/>).
/// </remarks>
public sealed class ListRunningTasksQueryHandler
{
    /// <summary>The Tasks module's database context, and this module's read boundary.</summary>
    private readonly TasksDbContext _dbContext;

    /// <summary>The authenticated principal, whose administrator status decides whether logs are sent.</summary>
    private readonly ICurrentUser _currentUser;

    /// <summary>Names each task's kind as an operator reads it, in the request's culture.</summary>
    private readonly TaskKindDisplayNames _kindNames;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Tasks module's database context.</param>
    /// <param name="currentUser">The authenticated principal of the current request.</param>
    /// <param name="kindNames">Names a task's kind for the request's culture.</param>
    public ListRunningTasksQueryHandler(
        TasksDbContext dbContext,
        ICurrentUser currentUser,
        TaskKindDisplayNames kindNames)
    {
        _dbContext = dbContext;
        _currentUser = currentUser;
        _kindNames = kindNames;
    }

    /// <summary>Returns every running task the caller may see, newest first.</summary>
    /// <param name="query">The (parameterless) request.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The running tasks; never a failure.</returns>
    public async Task<Result<IReadOnlyList<PanelTaskDto>>> HandleAsync(
        ListRunningTasksQuery query,
        CancellationToken cancellationToken)
    {
        var running = await _dbContext.PanelTasks
            .AsNoTracking()
            .Where(task => task.Status == PanelTaskStatus.Running)
            .OrderByDescending(task => task.StartedAt)
            .ThenBy(task => task.Id)
            .ToListAsync(cancellationToken);

        var includeLog = _currentUser.IsAdmin;
        IReadOnlyList<PanelTaskDto> projected = running
            .Select(task =>
            {
                return PanelTaskMapper.From(task, _kindNames.Of(task.Kind), includeLog);
            })
            .ToList();
        return Result<IReadOnlyList<PanelTaskDto>>.Ok(projected);
    }
}
