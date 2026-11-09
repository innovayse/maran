using Maran.Modules.Tasks.Domain.Entities;
using Maran.Modules.Tasks.Persistence;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Microsoft.Extensions.Logging;
using Wolverine;

namespace Maran.Modules.Tasks.Services;

/// <summary>
/// The panel's <see cref="ITaskRecorder"/>: writes a <see cref="PanelTask"/> row for an operation
/// another module is running, and swallows every failure of its own into a log line.
/// </summary>
/// <remarks>
/// <para>
/// <b>Nothing this class does may reach the operation it records.</b> Its callers are in the middle
/// of deleting an account, replacing a certificate on a live site, ordering from a certificate
/// authority — work that is destructive, partly done, and correct without any of this. A full disk,
/// a database that has gone away or a bug in this file must cost an operator the progress bar and
/// nothing else: an operation that would have succeeded still succeeds, and one that would have
/// failed still fails with the same error reaching the same caller. That is the single property this
/// class exists to hold, and it is why every method's body is a <c>try</c>.
/// </para>
/// <para>
/// <b>Cancellation is swallowed with the rest, and that is not an oversight.</b> Rethrowing it would
/// be this class throwing into its caller — the exact thing forbidden above — and it would do so on
/// the path where the caller is least able to cope, mid-shutdown. The caller's own token is what
/// stops the caller; a recording that was cancelled is a recording that did not happen, which is a
/// log line like any other.
/// </para>
/// <para>
/// <b>The catches are deliberately broad, and this is one of the places rules/csharp.md's "no
/// swallowing exceptions" gives way to a stated reason rather than being bent quietly.</b> Nothing
/// is silent: every failure is logged at warning with the task and the operation, which is the
/// operator's evidence that the pane is missing an update rather than the update never happening.
/// The alternative — catching the four or five exception types EF Core and Npgsql are known to
/// raise today — is a list that goes stale, and its going stale looks exactly like an account
/// deletion failing for no reason a customer can be told.
/// </para>
/// <para>
/// <b>Its own <see cref="TasksDbContext"/>, saved on every call.</b> A task must be visible while
/// the operation is still running, so its writes cannot ride along on the caller's transaction and
/// appear only at the end — which would also mean a failed operation rolling back the record of its
/// own failure, the one row an operator most needs.
/// </para>
/// </remarks>
public sealed class TaskRecorder : ITaskRecorder
{
    /// <summary>Pre-compiled log delegate for a recording that could not be written.</summary>
    /// <remarks>
    /// Warning, not Error: nothing is broken on the server, and the operation itself is unaffected.
    /// The operation name is carried so a reader can tell "the task was never opened" from "the task
    /// is stuck at forty percent because its completion was not written".
    /// </remarks>
    private static readonly Action<ILogger, string, string, Exception?> LogRecordingFailed =
        LoggerMessage.Define<string, string>(
            LogLevel.Warning,
            new EventId(1, nameof(TaskRecorder)),
            "Recording {Operation} for panel task {TaskId} failed; the operation itself is unaffected.");

    /// <summary>The Tasks module's database context.</summary>
    private readonly TasksDbContext _dbContext;

    /// <summary>The injected time source; never the ambient clock (rules/csharp.md).</summary>
    private readonly IClock _clock;

    /// <summary>Where a failed recording becomes visible to an operator.</summary>
    private readonly ILogger<TaskRecorder> _logger;

    /// <summary>Where a task's failure is announced to the administrators' notification feed.</summary>
    private readonly IMessageBus _bus;

    /// <summary>Creates the recorder.</summary>
    /// <param name="dbContext">The Tasks module's database context.</param>
    /// <param name="clock">The injected time source stamping each task.</param>
    /// <param name="logger">Sink for recordings that could not be written.</param>
    /// <param name="bus">The bus a failed task is announced on.</param>
    public TaskRecorder(TasksDbContext dbContext, IClock clock, ILogger<TaskRecorder> logger, IMessageBus bus)
    {
        _dbContext = dbContext;
        _clock = clock;
        _logger = logger;
        _bus = bus;
    }

    /// <inheritdoc />
    public async Task<Guid> BeginAsync(
        string kind,
        string subject,
        Guid? accountId,
        TaskAudience audience,
        string? correlationId,
        CancellationToken cancellationToken)
    {
        var task = new PanelTask(Guid.NewGuid(), kind, subject, accountId, audience, correlationId, _clock.UtcNow);

        try
        {
            _dbContext.PanelTasks.Add(task);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return task.Id;
        }
        catch (Exception exception)
        {
            // The entry is detached so that a later call on this same scoped context does not try
            // to save the row that just failed a second time, and report a failure the caller has
            // already been told about.
            _dbContext.Entry(task).State = EntityState.Detached;
            LogRecordingFailed(_logger, nameof(BeginAsync), task.Id.ToString(), exception);
            return Guid.Empty;
        }
    }

    /// <inheritdoc />
    public async Task ReportAsync(Guid taskId, int percent, string line, CancellationToken cancellationToken)
    {
        await MutateAsync(
            taskId,
            nameof(ReportAsync),
            task =>
            {
                task.Report(percent, line);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task CompleteAsync(Guid taskId, CancellationToken cancellationToken)
    {
        var at = _clock.UtcNow;
        await MutateAsync(
            taskId,
            nameof(CompleteAsync),
            task =>
            {
                task.Complete(at);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// <b>A failure — and only a failure — is announced.</b> Once the failure is stored, a
    /// <see cref="PanelNotificationRaised"/> is published for the administrators' feed. Completions
    /// are not announced: success already shows on the tasks page, and a feed that listed every
    /// finished operation would bury the ones that need a person. A second failure report for a task
    /// that already finished is refused by the entity and is not announced either.
    /// </para>
    /// <para>
    /// Published AFTER the commit, so the feed never announces a failure that was not stored, on a
    /// non-durable queue: a notification lost in the instant a process dies is acceptable because
    /// the task row and the tasks page are the record. The publish is inside this class's
    /// never-throw rule — a bus that fails costs the feed an entry and the operation nothing.
    /// </para>
    /// </remarks>
    public async Task FailAsync(Guid taskId, string errorCode, CancellationToken cancellationToken)
    {
        var at = _clock.UtcNow;
        var failedNow = false;
        var stored = await MutateAsync(
            taskId,
            nameof(FailAsync),
            task =>
            {
                failedNow = task.Fail(errorCode, at);
            },
            cancellationToken);

        if (stored is not null && failedNow)
        {
            await AnnounceFailureAsync(stored, cancellationToken);
        }
    }

    /// <summary>Publishes the notification for a task that has just been stored as failed.</summary>
    /// <param name="task">The failed task.</param>
    /// <param name="cancellationToken">Cancels the publish.</param>
    /// <returns>Resolves once published, or once the failure to publish has been logged.</returns>
    private async Task AnnounceFailureAsync(PanelTask task, CancellationToken cancellationToken)
    {
        try
        {
            await _bus.PublishAsync(new PanelNotificationRaised(
                NotificationSource.Task,
                task.ErrorCode ?? string.Empty,
                NotificationSeverity.Warning,
                task.Subject,
                [],
                NotificationDeepLinks.Tasks,
                task.FinishedAt ?? _clock.UtcNow));
        }
        catch (Exception exception)
        {
            LogRecordingFailed(_logger, nameof(AnnounceFailureAsync), task.Id.ToString(), exception);
        }
    }

    /// <summary>Loads one task, applies a change to it and saves — swallowing every failure.</summary>
    /// <param name="taskId">The task to change.</param>
    /// <param name="operation">The recorder method being performed, for the log line.</param>
    /// <param name="change">What to do to the task. The entity decides whether it applies.</param>
    /// <param name="cancellationToken">Cancellation token for the read and the write.</param>
    /// <returns>The stored task, or <c>null</c> when it was not found or its change could not be stored.</returns>
    /// <remarks>
    /// <see cref="Guid.Empty"/> is the id <see cref="BeginAsync"/> answers when it could not record
    /// anything, and it short-circuits here so that an operation whose task was never opened costs
    /// no database round trip per stage.
    ///
    /// The read deliberately IGNORES the module's tenant query filter. This runs inside
    /// somebody else's handler, whose principal may be an administrator, a customer, or — for the
    /// unattended renewal pass — nobody at all, and the row must be found in every one of those
    /// cases. It is safe because nothing read here is returned: the task is loaded by its own id,
    /// which only the operation that opened it holds, and the only thing that happens to it is the
    /// change the operation asked for.
    /// </remarks>
    private async Task<PanelTask?> MutateAsync(
        Guid taskId,
        string operation,
        Action<PanelTask> change,
        CancellationToken cancellationToken)
    {
        if (taskId == Guid.Empty)
        {
            return null;
        }

        try
        {
#pragma warning disable RS0030 // a task is recorded on behalf of a handler whose principal is not the task's own
            var task = await _dbContext.PanelTasks
                .IgnoreQueryFilters()
                .SingleOrDefaultAsync(candidate => candidate.Id == taskId, cancellationToken);
#pragma warning restore RS0030
            if (task is null)
            {
                return null;
            }

            change(task);
            await _dbContext.SaveChangesAsync(cancellationToken);

            return task;
        }
        catch (Exception exception)
        {
            LogRecordingFailed(_logger, operation, taskId.ToString(), exception);
            return null;
        }
    }
}
