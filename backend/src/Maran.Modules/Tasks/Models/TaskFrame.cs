using Maran.Modules.Tasks.Common;

namespace Maran.Modules.Tasks.Models;

/// <summary>
/// One thing to write to a watching client: either the task as it now stands, or the ending of the
/// stream. The two travel as one type so the writer consumes a single sequence and cannot forget the
/// ending.
/// </summary>
/// <param name="Snapshot">The task as it now stands, when this frame is an update; <c>null</c> for an ending.</param>
/// <param name="End">
/// The ending, carrying the final status the task reached and its name in the request's culture, or
/// <c>null</c> when this frame is an update. A sequence of frames ends with exactly one frame whose
/// ending is set.
/// </param>
public sealed record TaskFrame(PanelTaskDto? Snapshot, TaskStreamEndDto? End)
{
    /// <summary>Builds a frame carrying the task as it now stands.</summary>
    /// <param name="snapshot">The task's current state.</param>
    /// <returns>The update frame.</returns>
    public static TaskFrame OfTask(PanelTaskDto snapshot)
    {
        return new TaskFrame(snapshot, null);
    }

    /// <summary>Builds the frame that ends a stream.</summary>
    /// <param name="end">The final status the task reached, with its localized name.</param>
    /// <returns>The terminal frame.</returns>
    public static TaskFrame OfEnd(TaskStreamEndDto end)
    {
        return new TaskFrame(null, end);
    }
}
