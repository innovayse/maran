using Maran.Sdk.Interfaces;

namespace Maran.Modules.Accounts.Tests.TestSupport;

/// <summary>
/// An <see cref="ITaskRecorder"/> double that keeps what an instrumented handler recorded, so a test
/// can assert how many tasks an operation opened and what it closed them as.
/// </summary>
/// <remarks>
/// It records rather than verifies: the shape of the instrumentation — one task per invocation,
/// closed under the code the caller was answered with — is what the handler owes, and the real
/// recorder's own behaviour (the clamp, the cap, the refusal to reopen a finished task) is pinned
/// where that lives, in the Tasks module's own suite.
///
/// It never throws, exactly as the contract requires of every implementation. A double that threw
/// would be testing a promise the interface makes to nobody.
/// </remarks>
public sealed class RecordingTaskRecorder : ITaskRecorder
{
    /// <summary>Every task opened, in the order they were opened.</summary>
    public List<RecordedTask> Tasks { get; } = [];

    /// <inheritdoc />
    public Task<Guid> BeginAsync(
        string kind,
        string subject,
        string? correlationId,
        CancellationToken cancellationToken)
    {
        var recorded = new RecordedTask(Guid.NewGuid(), kind, subject, correlationId);
        Tasks.Add(recorded);
        return Task.FromResult(recorded.Id);
    }

    /// <inheritdoc />
    public Task ReportAsync(Guid taskId, int percent, string line, CancellationToken cancellationToken)
    {
        Find(taskId)?.Reports.Add((percent, line));
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task CompleteAsync(Guid taskId, CancellationToken cancellationToken)
    {
        var task = Find(taskId);
        if (task is not null)
        {
            task.Completed = true;
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task FailAsync(Guid taskId, string errorCode, CancellationToken cancellationToken)
    {
        var task = Find(taskId);
        if (task is not null)
        {
            task.FailureCode = errorCode;
        }

        return Task.CompletedTask;
    }

    /// <summary>Finds one recorded task, or null for the empty id and for one never opened.</summary>
    /// <param name="taskId">The task to find.</param>
    /// <returns>The recorded task, or null.</returns>
    private RecordedTask? Find(Guid taskId)
    {
        return Tasks.SingleOrDefault(task =>
        {
            return task.Id == taskId;
        });
    }
}
