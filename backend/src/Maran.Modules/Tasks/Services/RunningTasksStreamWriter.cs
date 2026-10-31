using Maran.Modules.Tasks.Common;
using Maran.Modules.Tasks.Models;
using Maran.Modules.Tasks.Options;
using Maran.Sdk.Streaming;
using Microsoft.Extensions.Options;

namespace Maran.Modules.Tasks.Services;

/// <summary>
/// Writes the shell's running-tasks stream as server-sent events: a <c>task</c> event per changed
/// task and a <c>running</c> event whenever the set of running tasks changes, with heartbeats between.
/// </summary>
/// <remarks>
/// The transport is <see cref="EventStreamWriter"/>, shared by every streaming module; this type only
/// renders this stream's two frame kinds. The <c>task</c> payload is the same <see cref="PanelTaskDto"/>
/// the per-task stream sends, so the SPA reads both with one decoder.
/// </remarks>
public sealed class RunningTasksStreamWriter
{
    /// <summary>How long the stream may be silent before a heartbeat is written.</summary>
    private readonly TimeSpan _heartbeatInterval;

    /// <summary>Creates the writer.</summary>
    /// <param name="options">The task stream's settings, chiefly the heartbeat interval.</param>
    public RunningTasksStreamWriter(IOptions<TaskStreamOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _heartbeatInterval = TimeSpan.FromSeconds(options.Value.HeartbeatSeconds);
    }

    /// <summary>Streams every frame to the response, flushing each one, heartbeating between them.</summary>
    /// <param name="response">The response to write to; its headers are set here and not before.</param>
    /// <param name="frames">The frames to write; the sequence ends only when the subscriber leaves.</param>
    /// <param name="cancellationToken">Cancelled when the subscriber goes away.</param>
    /// <returns>Resolves when the stream has ended.</returns>
    public Task WriteAsync(
        HttpResponse response,
        IAsyncEnumerable<RunningTasksFrame> frames,
        CancellationToken cancellationToken)
    {
        return EventStreamWriter.WriteAsync(response, frames, Format, _heartbeatInterval, cancellationToken);
    }

    /// <summary>Renders one frame as a server-sent event.</summary>
    /// <param name="frame">The frame to render.</param>
    /// <returns>The event's wire text, terminated by the blank line that ends an event.</returns>
    private static string Format(RunningTasksFrame frame)
    {
        if (frame.RunningIds is null)
        {
            return EventStreamFrame.Render("task", frame.Snapshot!);
        }

        return EventStreamFrame.Render("running", new RunningTaskIdsDto(frame.RunningIds));
    }
}
