using Maran.Modules.Tasks.Common;
using Maran.Modules.Tasks.Mappers;
using Maran.Modules.Tasks.Persistence;
using Maran.Modules.Tasks.Resources;

namespace Maran.Modules.Tasks.Queries.ListTasks;

/// <summary>
/// Handles <see cref="ListTasksQuery"/> by reading the newest rows of <c>tasks.PanelTasks</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>A customer is answered "not found", not "here is an empty list".</b> The whole surface is
/// administrator-only (R14), so the honest answer to a customer asking for it is that it does not
/// exist for them — the same answer they get for a single task, and the same 404-never-403 rule
/// applied to a collection. An empty 200 would say something different and slightly worse: that
/// there is a tasks feed here and they simply have nothing in it.
/// </para>
/// <para>
/// This check is NOT the module's authorisation mechanism and does not mask one. The context's own
/// query filter is what makes a customer's read return no rows, and it holds whether or not this
/// method remembers anything; what this adds is only the shape of the answer for a collection,
/// which a filter cannot express because an empty result set is a perfectly ordinary listing.
/// Remove the filter and the single reads and the stream leak; remove this and the listing answers
/// 200 with an empty array. Each is measurable on its own.
/// </para>
/// </remarks>
public sealed class ListTasksQueryHandler
{
    /// <summary>
    /// How many tasks the listing returns. The pane shows recent work rather than a history, and
    /// the table is append-only under every instrumented operation on the server — so an unbounded
    /// read is one that gets slower forever while showing nobody anything they scrolled to.
    /// </summary>
    private const int MaxTasks = 200;

    /// <summary>The Tasks module's database context, and this module's read boundary.</summary>
    private readonly TasksDbContext _dbContext;

    /// <summary>The authenticated principal, whose administrator status the surface requires.</summary>
    private readonly ICurrentUser _currentUser;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Tasks module's database context.</param>
    /// <param name="currentUser">The authenticated principal of the current request.</param>
    public ListTasksQueryHandler(TasksDbContext dbContext, ICurrentUser currentUser)
    {
        _dbContext = dbContext;
        _currentUser = currentUser;
    }

    /// <summary>Returns the most recent tasks, newest first.</summary>
    /// <param name="query">The (parameterless) list request.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The tasks, or <c>TaskNotFound</c> for a caller the surface does not exist for.</returns>
    public async Task<Result<IReadOnlyList<PanelTaskDto>>> HandleAsync(
        ListTasksQuery query,
        CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAdmin)
        {
            return Result<IReadOnlyList<PanelTaskDto>>.Fail(Error.Of(nameof(ErrorMessages.TaskNotFound), ErrorType.NotFound));
        }

        var tasks = await _dbContext.PanelTasks
            .AsNoTracking()
            .OrderByDescending(task => task.StartedAt)
            .Take(MaxTasks)
            .ToListAsync(cancellationToken);

        IReadOnlyList<PanelTaskDto> projected = tasks.Select(PanelTaskMapper.From).ToList();
        return Result<IReadOnlyList<PanelTaskDto>>.Ok(projected);
    }
}
