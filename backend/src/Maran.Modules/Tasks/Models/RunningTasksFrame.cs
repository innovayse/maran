using Maran.Modules.Tasks.Common;

namespace Maran.Modules.Tasks.Models;

/// <summary>
/// One thing to write to the shell's running-tasks stream: either one task as it now stands, or the
/// ids of every running task the subscriber may see. The two travel as one type so the writer
/// consumes a single sequence.
/// </summary>
/// <param name="Snapshot">The task as it now stands, when this frame is an update; <c>null</c> for a set.</param>
/// <param name="RunningIds">
/// The ids of every running task the subscriber may see, when this frame announces the set;
/// <c>null</c> for an update. Exactly one of the two is set.
/// </param>
public sealed record RunningTasksFrame(PanelTaskDto? Snapshot, IReadOnlyList<Guid>? RunningIds)
{
    /// <summary>Builds a frame carrying one task as it now stands.</summary>
    /// <param name="snapshot">The task's current state.</param>
    /// <returns>The update frame.</returns>
    public static RunningTasksFrame OfTask(PanelTaskDto snapshot)
    {
        return new RunningTasksFrame(snapshot, null);
    }

    /// <summary>Builds the frame announcing which tasks are running now.</summary>
    /// <param name="runningIds">Every running task the subscriber may see.</param>
    /// <returns>The set frame.</returns>
    public static RunningTasksFrame OfRunning(IReadOnlyList<Guid> runningIds)
    {
        return new RunningTasksFrame(null, runningIds);
    }
}
