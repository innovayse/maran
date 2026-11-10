using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Marketplace.Tests.TestSupport;

/// <summary>A task recorder that remembers everything it was told.</summary>
public sealed class FakeTaskRecorder : ITaskRecorder
{
    /// <summary>The tasks begun, in order.</summary>
    public List<RecordedTask> Begun { get; } = [];

    /// <summary>The progress lines reported, in order.</summary>
    public List<RecordedTaskLine> Lines { get; } = [];

    /// <summary>The ids of the tasks completed.</summary>
    public List<Guid> Completed { get; } = [];

    /// <summary>The tasks failed, with the code each was failed with.</summary>
    public List<(Guid TaskId, string Code)> Failed { get; } = [];

    /// <inheritdoc />
    public Task<Guid> BeginAsync(
        string kind,
        string subject,
        Guid? accountId,
        TaskAudience audience,
        string? correlationId,
        CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid();
        Begun.Add(new RecordedTask(id, kind, subject, audience));
        return Task.FromResult(id);
    }

    /// <inheritdoc />
    public Task ReportAsync(Guid taskId, int percent, string line, CancellationToken cancellationToken)
    {
        Lines.Add(new RecordedTaskLine(taskId, percent, line));
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task CompleteAsync(Guid taskId, CancellationToken cancellationToken)
    {
        Completed.Add(taskId);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task FailAsync(Guid taskId, string errorCode, CancellationToken cancellationToken)
    {
        Failed.Add((taskId, errorCode));
        return Task.CompletedTask;
    }
}
