using Maran.Modules.Tasks.Common;
using Maran.Modules.Tasks.Mappers;
using Maran.Modules.Tasks.Persistence;
using Maran.Modules.Tasks.Resources;
using Maran.Modules.Tasks.Services;

namespace Maran.Modules.Tasks.Queries.GetTask;

/// <summary>Handles <see cref="GetTaskQuery"/> by reading one row of <c>tasks.PanelTasks</c>.</summary>
/// <remarks>
/// There is no authorisation check here, and deliberately not one: the context's query filter
/// supplies it, so a caller the surface does not exist for finds no row and is answered
/// <c>TaskNotFound</c> — a 404 that confirms nothing, rather than a 403 that confirms the task is
/// real (spec §8). This handler could not leak a task even if it were rewritten carelessly.
/// What it does decide is whether the log is sent: to an administrator only
/// (<see cref="PanelTaskMapper"/>).
/// </remarks>
public sealed class GetTaskQueryHandler
{
    /// <summary>The Tasks module's database context, and this module's read boundary.</summary>
    private readonly TasksDbContext _dbContext;

    /// <summary>Names the task's kind as an operator reads it, in the request's culture.</summary>
    private readonly TaskKindDisplayNames _kindNames;

    /// <summary>The authenticated principal, whose administrator status decides whether the log is sent.</summary>
    private readonly ICurrentUser _currentUser;

    /// <summary>Creates the handler with the module's own database context.</summary>
    /// <param name="dbContext">The Tasks module's database context.</param>
    /// <param name="kindNames">Names a task's kind for the request's culture.</param>
    /// <param name="currentUser">The authenticated principal of the current request.</param>
    public GetTaskQueryHandler(TasksDbContext dbContext, TaskKindDisplayNames kindNames, ICurrentUser currentUser)
    {
        _dbContext = dbContext;
        _kindNames = kindNames;
        _currentUser = currentUser;
    }

    /// <summary>Returns one task.</summary>
    /// <param name="query">Which task to read.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The task, or <c>TaskNotFound</c>.</returns>
    public async Task<Result<PanelTaskDto>> HandleAsync(GetTaskQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var task = await _dbContext.PanelTasks
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == query.TaskId, cancellationToken);

        return task is null
            ? Result<PanelTaskDto>.Fail(Error.Of(nameof(ErrorMessages.TaskNotFound), ErrorType.NotFound))
            : Result<PanelTaskDto>.Ok(PanelTaskMapper.From(task, _kindNames.Of(task.Kind), _currentUser.IsAdmin));
    }
}
