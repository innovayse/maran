using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Updates.Tests.TestSupport;

/// <summary>Keeps every call made to the task recorder, in order, as short readable strings.</summary>
public sealed class RecordingTaskRecorder : ITaskRecorder
{
    /// <summary>The id <see cref="BeginAsync"/> answers; <see cref="Guid.Empty"/> makes the recorder behave as one that could not record.</summary>
    public Guid NextTaskId { get; set; } = Guid.Parse("11111111-2222-4333-8444-555555555555");

    /// <summary>Every call, such as <c>begin PanelUpdate</c> or <c>fail PanelUpdateRolledBack</c>.</summary>
    public List<string> Calls { get; } = [];

    /// <summary>The task kind of the last <see cref="BeginAsync"/>.</summary>
    public string? BegunKind { get; private set; }

    /// <summary>The audience of the last <see cref="BeginAsync"/>.</summary>
    public TaskAudience? BegunAudience { get; private set; }

    /// <summary>The account id of the last <see cref="BeginAsync"/>.</summary>
    public Guid? BegunAccountId { get; private set; }

    /// <inheritdoc />
    public Task<Guid> BeginAsync(
        string kind,
        string subject,
        Guid? accountId,
        TaskAudience audience,
        string? correlationId,
        CancellationToken cancellationToken)
    {
        BegunKind = kind;
        BegunAudience = audience;
        BegunAccountId = accountId;
        Calls.Add($"begin {kind}");
        return Task.FromResult(NextTaskId);
    }

    /// <inheritdoc />
    public Task ReportAsync(Guid taskId, int percent, string line, CancellationToken cancellationToken)
    {
        Calls.Add($"report {taskId} {line}");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task CompleteAsync(Guid taskId, CancellationToken cancellationToken)
    {
        Calls.Add($"complete {taskId}");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task FailAsync(Guid taskId, string errorCode, CancellationToken cancellationToken)
    {
        Calls.Add($"fail {taskId} {errorCode}");
        return Task.CompletedTask;
    }
}
