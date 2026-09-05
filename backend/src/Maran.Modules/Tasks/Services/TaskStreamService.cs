using System.Runtime.CompilerServices;
using Maran.Modules.Tasks.Domain.Enums;
using Maran.Modules.Tasks.Mappers;
using Maran.Modules.Tasks.Models;
using Maran.Modules.Tasks.Options;
using Maran.Modules.Tasks.Persistence;
using Maran.Modules.Tasks.Resources;
using Microsoft.Extensions.Options;

namespace Maran.Modules.Tasks.Services;

/// <summary>
/// Resolves a request to watch one task and produces the frames a watcher is sent: the task as it
/// stands now, one more frame every time it changes, and exactly one ending.
/// </summary>
/// <remarks>
/// <para>
/// <b>Resolving and reading are two calls on purpose.</b> The controller must be able to answer 404
/// with an ordinary JSON problem response, and it can only do that before the first byte of the
/// stream goes out — after that the status line is already on the wire and an error can only be
/// expressed inside the stream, which is where a "not found" becomes a stream that opens and
/// immediately says nothing. So <see cref="ResolveAsync"/> settles existence and visibility while a
/// normal response is still possible, and <see cref="ReadAsync"/> only ever runs for a task the
/// caller may watch.
/// </para>
/// <para>
/// <b>A frame is sent when the row's revision moves, and at no other time.</b> That is what makes a
/// long silent stage cost the connection its heartbeat comments instead of a frame per poll, and it
/// is why the entity counts its own changes rather than the reader comparing whole rows — a task
/// whose log is growing would compare unequal on every poll for reasons the watcher does not need
/// to see.
/// </para>
/// <para>
/// <b>The first frame is always sent, whatever the revision.</b> A watcher that attaches to a task
/// already at sixty percent must be told sixty percent immediately; waiting for the next change
/// would leave the pane empty for as long as the current stage lasts, which is precisely the stage
/// somebody opened the pane to watch.
/// </para>
/// <para>
/// <b>A task that vanishes ends the stream rather than hanging it.</b> A RUNNING task is never
/// deleted — <c>TaskRetentionHandler</c> only removes rows whose <c>FinishedAt</c> is already
/// weeks old — so a watcher cannot outlive the very task it just attached to. What this branch
/// guards against is a stream that outlives the row some other way: a client resuming an id it
/// saved days ago, or a task that finished and aged out of the retention window while nobody was
/// watching it. Either way, the alternative to having it is a reader that polls a row that will
/// never come back for as long as the operator leaves the pane open.
/// </para>
/// </remarks>
public sealed class TaskStreamService
{
    /// <summary>The Tasks module's database context, and this module's read boundary.</summary>
    private readonly TasksDbContext _dbContext;

    /// <summary>How often the row is re-read while the task is still running.</summary>
    private readonly TimeSpan _pollInterval;

    /// <summary>Creates the service.</summary>
    /// <param name="dbContext">The Tasks module's database context.</param>
    /// <param name="options">The stream's settings, chiefly the poll interval.</param>
    public TaskStreamService(TasksDbContext dbContext, IOptions<TaskStreamOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _dbContext = dbContext;
        _pollInterval = TimeSpan.FromMilliseconds(options.Value.PollIntervalMilliseconds);
    }

    /// <summary>Settles whether the caller may watch this task, while a normal response is still possible.</summary>
    /// <param name="taskId">The task the caller asked to watch.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The task's id, or <c>TaskNotFound</c> — never 403, which would confirm it exists.</returns>
    public async Task<Result<Guid>> ResolveAsync(Guid taskId, CancellationToken cancellationToken)
    {
        var exists = await _dbContext.PanelTasks
            .AsNoTracking()
            .AnyAsync(task => task.Id == taskId, cancellationToken);

        return exists
            ? Result<Guid>.Ok(taskId)
            : Result<Guid>.Fail(Error.Of(nameof(ErrorMessages.TaskNotFound), ErrorType.NotFound));
    }

    /// <summary>Produces the frames for one watched task, ending with exactly one terminal frame.</summary>
    /// <param name="taskId">The task to watch, as <see cref="ResolveAsync"/> returned it.</param>
    /// <param name="cancellationToken">Cancelled when the watcher goes away.</param>
    /// <returns>Update frames while the task changes, then one ending.</returns>
    public async IAsyncEnumerable<TaskFrame> ReadAsync(
        Guid taskId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var lastRevision = -1;

        while (true)
        {
            var task = await _dbContext.PanelTasks
                .AsNoTracking()
                .SingleOrDefaultAsync(candidate => candidate.Id == taskId, cancellationToken);
            if (task is null)
            {
                yield break;
            }

            if (task.Revision != lastRevision)
            {
                lastRevision = task.Revision;
                yield return TaskFrame.OfTask(PanelTaskMapper.From(task));
            }

            if (task.Status != PanelTaskStatus.Running)
            {
                yield return TaskFrame.OfEnd(task.Status);
                yield break;
            }

            await Task.Delay(_pollInterval, cancellationToken);
        }
    }
}
