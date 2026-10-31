using System.Runtime.CompilerServices;
using Maran.Modules.Tasks.Common;
using Maran.Modules.Tasks.Domain.Entities;
using Maran.Modules.Tasks.Domain.Enums;
using Maran.Modules.Tasks.Mappers;
using Maran.Modules.Tasks.Models;
using Maran.Modules.Tasks.Options;
using Maran.Modules.Tasks.Persistence;
using Microsoft.Extensions.Options;

namespace Maran.Modules.Tasks.Services;

/// <summary>
/// Produces the frames of the shell's ONE running-tasks stream: every running task the subscriber may
/// see as it stands, a frame for each of them every time it changes — the change that finishes it
/// included — and the set of running ids whenever that set changes.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why one stream and not one per task.</b> The shell used to open the per-task stream for every
/// running task, which made the connections a browser holds grow with the work in flight on the host.
/// This stream multiplexes them: one request, one database poll per interval, whatever the number of
/// tasks. The per-task stream (<see cref="TaskStreamService"/>) stays for the live pane, because the
/// pane watches ONE task that may long since have finished, which this stream never carries.
/// </para>
/// <para>
/// <b>Visibility is the context's query filter, exactly as everywhere else.</b> Both reads below go
/// through <c>PanelTasks</c> with the subscriber's principal, so a customer is streamed the tasks of
/// their own account with the <c>AccountOwner</c> audience and nothing else, and a finished task is
/// re-read through the same filter — a row that stopped being visible is not sent, its id only
/// leaves the set. The log follows the subscriber's role on every frame, as the list decides it.
/// </para>
/// <para>
/// <b>The set frame is what lets a subscriber forget.</b> A finished task is sent whole once more so
/// the subscriber sees how it ended; a task that vanished or became invisible cannot be, so the ids
/// are announced whenever membership changes, and once when the stream opens — after the opening
/// task frames — which is also what reconciles a subscriber that reconnects.
/// </para>
/// <para>
/// <b>It never ends on its own.</b> Running work comes and goes for as long as the shell is open, so
/// the sequence ends only when the subscriber's token is cancelled. It polls rather than being pushed
/// to for the reason <see cref="TaskStreamOptions.PollIntervalMilliseconds"/> states.
/// </para>
/// <para>
/// <b>Language per subscriber.</b> Kind names and failure sentences are resolved through the scoped
/// <see cref="TaskKindDisplayNames"/> and <see cref="TaskFailureMessages"/> of the request that opened
/// the stream, exactly as the per-task stream resolves them, so each subscriber reads its frames in its
/// own language; a client that switches language reopens the stream.
/// </para>
/// </remarks>
public sealed class RunningTasksStreamService
{
    /// <summary>The Tasks module's database context, filtered to what the subscriber may see.</summary>
    private readonly TasksDbContext _dbContext;

    /// <summary>How often the running tasks are re-read.</summary>
    private readonly TimeSpan _pollInterval;

    /// <summary>Names a task's kind in the culture of the request that opened the stream.</summary>
    private readonly TaskKindDisplayNames _kindNames;

    /// <summary>Describes a failed task's code as a sentence, in the request's culture.</summary>
    private readonly TaskFailureMessages _failures;

    /// <summary>The subscriber, whose administrator status decides whether frames carry the log.</summary>
    private readonly ICurrentUser _currentUser;

    /// <summary>Creates the service.</summary>
    /// <param name="dbContext">The Tasks module's database context.</param>
    /// <param name="options">The task stream's settings, chiefly the poll interval.</param>
    /// <param name="kindNames">Names a task's kind for the culture of the request that opened the stream.</param>
    /// <param name="failures">
    /// Describes a failed task's code for the culture of the request that opened the stream, so each
    /// subscriber reads its frames in its own language.
    /// </param>
    /// <param name="currentUser">The authenticated principal subscribing.</param>
    public RunningTasksStreamService(
        TasksDbContext dbContext,
        IOptions<TaskStreamOptions> options,
        TaskKindDisplayNames kindNames,
        TaskFailureMessages failures,
        ICurrentUser currentUser)
    {
        ArgumentNullException.ThrowIfNull(options);

        _dbContext = dbContext;
        _pollInterval = TimeSpan.FromMilliseconds(options.Value.PollIntervalMilliseconds);
        _kindNames = kindNames;
        _failures = failures;
        _currentUser = currentUser;
    }

    /// <summary>Produces the frames until the subscriber goes away.</summary>
    /// <param name="cancellationToken">Cancelled when the subscriber disconnects.</param>
    /// <returns>Task frames and running-set frames, never ending on their own.</returns>
    public async IAsyncEnumerable<RunningTasksFrame> ReadAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // The revision each held task was last sent at, keyed by id: a task is sent again only when
        // its revision moves, as on the per-task stream.
        var sent = new Dictionary<Guid, int>();
        var announced = false;

        while (true)
        {
            var running = await _dbContext.PanelTasks
                .AsNoTracking()
                .Where(task => task.Status == PanelTaskStatus.Running)
                .OrderByDescending(task => task.StartedAt)
                .ThenBy(task => task.Id)
                .ToListAsync(cancellationToken);

            var membershipChanged = false;
            foreach (var task in running)
            {
                if (!sent.TryGetValue(task.Id, out var revision))
                {
                    membershipChanged = true;
                }
                else if (revision == task.Revision)
                {
                    continue;
                }

                sent[task.Id] = task.Revision;
                yield return RunningTasksFrame.OfTask(Map(task));
            }

            var runningIds = running
                .Select(task =>
                {
                    return task.Id;
                })
                .ToList();
            var departed = sent.Keys.Except(runningIds).ToList();
            if (departed.Count > 0)
            {
                membershipChanged = true;
                var finished = await _dbContext.PanelTasks
                    .AsNoTracking()
                    .Where(task => departed.Contains(task.Id))
                    .ToListAsync(cancellationToken);
                foreach (var task in finished)
                {
                    yield return RunningTasksFrame.OfTask(Map(task));
                }

                foreach (var id in departed)
                {
                    sent.Remove(id);
                }
            }

            if (!announced || membershipChanged)
            {
                announced = true;
                yield return RunningTasksFrame.OfRunning(runningIds);
            }

            await Task.Delay(_pollInterval, cancellationToken);
        }
    }

    /// <summary>Maps one row as the subscriber may see it.</summary>
    /// <param name="task">The row.</param>
    /// <returns>
    /// The wire shape, its failure described in the subscriber's language, with the log only for an
    /// administrator.
    /// </returns>
    private PanelTaskDto Map(PanelTask task)
    {
        return PanelTaskMapper.From(
            task, _kindNames.Of(task.Kind), _failures.Describe(task.ErrorCode), _currentUser.IsAdmin);
    }
}
