namespace Maran.Sdk.Interfaces;

/// <summary>
/// Records a long-running operation as a panel task, so an operator watching the panel sees it
/// progress instead of watching a request that has not answered yet. The contract lives in the Sdk
/// because every module records into the same journal of tasks and no module may reference the one
/// that owns the table — the same reason <see cref="IAuditWriter"/> lives here.
/// </summary>
/// <remarks>
/// <para>
/// <b>No method on this interface may throw into the operation it records, ever.</b> A task is a
/// description of work, not the work: a full disk, a database that has gone away or a bug in the
/// recorder must cost an operator the progress bar and nothing else. An account deletion that would
/// otherwise have succeeded must still succeed, and one that would have failed must fail with the
/// same error it always did. Implementations therefore swallow every failure of their own into a log
/// line — including cancellation, because the caller's own token is what stops the caller, and a
/// recording that was cancelled is a recording that did not happen.
/// </para>
/// <para>
/// The consequence is visible in the signature: <see cref="BeginAsync"/> answers
/// <see cref="Guid.Empty"/> when it could not record anything, and every other method treats that id
/// as "there is no task", so an instrumented operation needs no null check and no branch of its own.
/// Instrumentation is a straight line of calls that can be read past.
/// </para>
/// </remarks>
public interface ITaskRecorder
{
    /// <summary>Opens a task for an operation that is starting.</summary>
    /// <param name="kind">
    /// What kind of operation this is, from <see cref="Contracts.TaskKinds"/>. Machine-stable and
    /// never a sentence: the SPA renders its own label for a kind it knows and the raw kind for one
    /// it does not.
    /// </param>
    /// <param name="subject">
    /// What the operation acts on, as an operator would search for it — a domain, an account name.
    /// Never a secret and never a customer-supplied command line: the tasks table is read by the
    /// operator and outlives the operation.
    /// </param>
    /// <param name="correlationId">
    /// The current request's correlation id, so a task can be lined up with the log lines and the
    /// audit entry of the request that started it, or <c>null</c> for unattended work that has no
    /// request behind it.
    /// </param>
    /// <param name="cancellationToken">Cancellation token for the write.</param>
    /// <returns>
    /// The new task's id, or <see cref="Guid.Empty"/> when the task could not be recorded. A caller
    /// passes whatever comes back to the other methods without inspecting it.
    /// </returns>
    Task<Guid> BeginAsync(string kind, string subject, string? correlationId, CancellationToken cancellationToken);

    /// <summary>Records progress against an open task.</summary>
    /// <param name="taskId">The task, as <see cref="BeginAsync"/> answered it.</param>
    /// <param name="percent">
    /// How far along the operation is. Clamped to 0–100 by the implementation, so a caller
    /// computing a percentage from a count it got wrong cannot store a progress bar nobody can draw.
    /// </param>
    /// <param name="line">
    /// One line of operator-facing English describing the stage just reached. Appended to the task's
    /// log, which is capped: past the cap the text is cut and marked, never grown without bound.
    /// </param>
    /// <param name="cancellationToken">Cancellation token for the write.</param>
    /// <returns>Resolves once the progress is stored, or once its failure has been logged.</returns>
    Task ReportAsync(Guid taskId, int percent, string line, CancellationToken cancellationToken);

    /// <summary>Closes a task that finished its work.</summary>
    /// <param name="taskId">The task, as <see cref="BeginAsync"/> answered it.</param>
    /// <param name="cancellationToken">Cancellation token for the write.</param>
    /// <returns>Resolves once the outcome is stored, or once its failure has been logged.</returns>
    Task CompleteAsync(Guid taskId, CancellationToken cancellationToken);

    /// <summary>Closes a task that did not finish its work.</summary>
    /// <param name="taskId">The task, as <see cref="BeginAsync"/> answered it.</param>
    /// <param name="errorCode">
    /// The machine-stable error code the operation answered its caller with, so the task and the
    /// response say the same thing. Never a sentence and never an agent's or an authority's own text.
    /// </param>
    /// <param name="cancellationToken">Cancellation token for the write.</param>
    /// <returns>Resolves once the outcome is stored, or once its failure has been logged.</returns>
    Task FailAsync(Guid taskId, string errorCode, CancellationToken cancellationToken);
}
