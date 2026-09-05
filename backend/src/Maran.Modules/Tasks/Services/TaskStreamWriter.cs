using Maran.Modules.Tasks.Common;
using Maran.Modules.Tasks.Models;
using Maran.Modules.Tasks.Options;
using Maran.Sdk.Streaming;
using Microsoft.Extensions.Options;

namespace Maran.Modules.Tasks.Services;

/// <summary>
/// Writes a sequence of <see cref="TaskFrame"/> to a client as server-sent events: one <c>task</c>
/// event each time the task changes, one final <c>end</c> event naming the status it reached, and a
/// comment frame whenever neither has been sent for a while.
/// </summary>
/// <remarks>
/// The transport itself — the framing, the heartbeat, the flushing and the shutdown ordering — is
/// <see cref="EventStreamWriter"/>, which every streaming module shares; why a stream must heartbeat
/// at all is written there once, because it is a fact about how the panel is deployed rather than
/// about panel tasks. This type holds only what belongs to this module: its settings, and how a task
/// frame becomes an event.
///
/// <para>What is specific to a task stream</para> is the length of its silence: a task says nothing
/// for exactly as long as its operation works without reporting, which for a certificate order
/// waiting on an authority is minutes. A watcher closing the pane ends the stream but not the
/// operation, which is the whole point of recording it.
/// </remarks>
public sealed class TaskStreamWriter
{
    /// <summary>The media type of a server-sent event stream.</summary>
    public const string EventStreamContentType = EventStreamWriter.EventStreamContentType;

    /// <summary>How long the stream may be silent before a heartbeat is written.</summary>
    private readonly TimeSpan _heartbeatInterval;

    /// <summary>Creates the writer.</summary>
    /// <param name="options">The stream's settings, chiefly the heartbeat interval.</param>
    public TaskStreamWriter(IOptions<TaskStreamOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _heartbeatInterval = TimeSpan.FromSeconds(options.Value.HeartbeatSeconds);
    }

    /// <summary>Streams every frame to the response, flushing each one, heartbeating between them.</summary>
    /// <param name="response">The response to write to; its headers are set here and not before.</param>
    /// <param name="frames">The frames to write, ending with exactly one terminal frame.</param>
    /// <param name="cancellationToken">Cancelled when the watching client goes away.</param>
    /// <returns>Resolves when the stream has ended, for whichever of its reasons.</returns>
    public Task WriteAsync(
        HttpResponse response,
        IAsyncEnumerable<TaskFrame> frames,
        CancellationToken cancellationToken)
    {
        return EventStreamWriter.WriteAsync(response, frames, Format, _heartbeatInterval, cancellationToken);
    }

    /// <summary>Renders one frame as a server-sent event.</summary>
    /// <param name="frame">The frame to render.</param>
    /// <returns>The event's wire text, terminated by the blank line that ends an event.</returns>
    /// <remarks>
    /// A task's log carries whatever the instrumented operation reported and its subject is a name a
    /// caller chose, so both are serialized rather than concatenated and cannot forge a frame of
    /// their own; the reasoning lives with the renderer (<see cref="EventStreamFrame"/>). The ending
    /// is discriminated by <see cref="TaskFrame.EndStatus"/>, whose counterpart
    /// <see cref="TaskFrame.Snapshot"/> is null on exactly those frames.
    /// </remarks>
    private static string Format(TaskFrame frame)
    {
        if (frame.EndStatus is null)
        {
            return EventStreamFrame.Render("task", frame.Snapshot!);
        }

        return EventStreamFrame.Render("end", new TaskStreamEndDto(frame.EndStatus.Value));
    }
}
