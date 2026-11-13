using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Apps.Tests.TestSupport;

/// <summary>An <see cref="ITaskStateReader"/> double with scripted states; a task nobody scripted is unknown, as in the real window.</summary>
public sealed class FakeTaskStateReader : ITaskStateReader
{
    /// <summary>The state each scripted task answers.</summary>
    public Dictionary<Guid, TaskState> States { get; } = [];

    /// <summary>The ids that were asked about, in order.</summary>
    public List<Guid> Asked { get; } = [];

    /// <summary>Runs while a task is being asked about, standing in for another request that writes the ledger in that window.</summary>
    public Func<Guid, Task>? WhileReading { get; set; }

    /// <inheritdoc />
    public async Task<TaskState> ReadAsync(Guid taskId, CancellationToken cancellationToken)
    {
        Asked.Add(taskId);
        if (WhileReading is { } rival)
        {
            await rival(taskId);
        }

        return States.TryGetValue(taskId, out var state) ? state : TaskState.Unknown;
    }
}
