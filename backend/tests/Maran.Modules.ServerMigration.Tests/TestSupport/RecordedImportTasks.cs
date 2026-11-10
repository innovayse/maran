using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.ServerMigration.Tests.TestSupport;

/// <summary>An <see cref="ITaskRecorder"/> that keeps what the import job reported, so a test can read it back.</summary>
public sealed class RecordedImportTasks : ITaskRecorder
{
    /// <summary>The tasks begun: kind, subject and audience.</summary>
    public List<(Guid Id, string Kind, string Subject, TaskAudience Audience)> Begun { get; } = [];

    /// <summary>The progress lines, in order.</summary>
    public List<(int Percent, string Line)> Reports { get; } = [];

    /// <summary>How many tasks were completed.</summary>
    public int Completed { get; private set; }

    /// <summary>The error codes tasks were failed with.</summary>
    public List<string> Failures { get; } = [];

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
        Begun.Add((id, kind, subject, audience));

        return Task.FromResult(id);
    }

    /// <inheritdoc />
    public Task ReportAsync(Guid taskId, int percent, string line, CancellationToken cancellationToken)
    {
        Reports.Add((percent, line));

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task CompleteAsync(Guid taskId, CancellationToken cancellationToken)
    {
        Completed++;

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task FailAsync(Guid taskId, string errorCode, CancellationToken cancellationToken)
    {
        Failures.Add(errorCode);

        return Task.CompletedTask;
    }
}
