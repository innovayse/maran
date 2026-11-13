using Maran.Sdk.Contracts;

namespace Maran.Sdk.Interfaces;

/// <summary>
/// Reads whether a panel task is still running, for a module that keeps a task's id in a row of its own
/// and must tell an operation in flight from one that ended without the row being told. The contract lives
/// in the Sdk and its implementation in the module that owns the tasks table, the same shape
/// <see cref="ITaskRecorder"/> has for writing (rules/architecture.md "A shared facility's contract lives in
/// Maran.Sdk").
/// </summary>
/// <remarks>
/// A read-only window that answers the narrowest value the question needs, a <see cref="TaskState"/>: no
/// subject, log, account or error code of the task travels with it. It is NOT tenant-scoped, because its
/// callers are unattended or administrator-only passes with the id taken from their own row, and the id is
/// a random Guid a caller must already hold; it must not be exposed to a request that supplies the id.
/// </remarks>
public interface ITaskStateReader
{
    /// <summary>Reads the state of one task.</summary>
    /// <param name="taskId">The task, as <see cref="ITaskRecorder.BeginAsync"/> answered it.</param>
    /// <param name="cancellationToken">Cancellation token for the read.</param>
    /// <returns>
    /// The task's state; <see cref="TaskState.Unknown"/> when no task has the id, which includes the empty
    /// id <see cref="ITaskRecorder.BeginAsync"/> answers when it could not record anything.
    /// </returns>
    Task<TaskState> ReadAsync(Guid taskId, CancellationToken cancellationToken);
}
