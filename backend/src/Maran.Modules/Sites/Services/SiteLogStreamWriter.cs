using Maran.Modules.Sites.Common;
using Maran.Modules.Sites.Models;
using Maran.Modules.Sites.Options;
using Maran.Sdk.Streaming;
using Microsoft.Extensions.Options;

namespace Maran.Modules.Sites.Services;

/// <summary>
/// Writes a sequence of <see cref="SiteLogFrame"/> to a client as server-sent events: one
/// <c>line</c> event per log line, one final <c>end</c> event naming why the stream stopped, and a
/// comment frame whenever neither has been sent for a while.
/// </summary>
/// <remarks>
/// The transport itself — the framing, the heartbeat, the flushing and the shutdown ordering — is
/// <see cref="EventStreamWriter"/>, which every streaming module shares; why a stream must heartbeat
/// at all is written there once, because it is a fact about how the panel is deployed rather than
/// about site logs. This type holds only what belongs to this module: its settings, and how a log
/// frame becomes an event.
///
/// <para>What is specific to a log tail</para> is the shape of its silence: a site with no traffic
/// produces nothing for hours, so the common case is exactly the one a proxy read timeout kills.
/// A client that reopens a tail gets a fresh history replay rather than a resumed position.
/// </remarks>
public sealed class SiteLogStreamWriter
{
    /// <summary>The media type of a server-sent event stream.</summary>
    public const string EventStreamContentType = EventStreamWriter.EventStreamContentType;

    /// <summary>How long the stream may be silent before a heartbeat is written.</summary>
    private readonly TimeSpan _heartbeatInterval;

    /// <summary>Creates the writer.</summary>
    /// <param name="options">The stream's settings, chiefly the heartbeat interval.</param>
    public SiteLogStreamWriter(IOptions<SiteLogOptions> options)
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
        IAsyncEnumerable<SiteLogFrame> frames,
        CancellationToken cancellationToken)
    {
        return EventStreamWriter.WriteAsync(response, frames, Format, _heartbeatInterval, cancellationToken);
    }

    /// <summary>Renders one frame as a server-sent event.</summary>
    /// <param name="frame">The frame to render.</param>
    /// <returns>The event's wire text, terminated by the blank line that ends an event.</returns>
    /// <remarks>
    /// A log line is a customer's own file content, so it is serialized rather than concatenated and
    /// cannot forge a frame of its own; the reasoning lives with the renderer
    /// (<see cref="EventStreamFrame"/>). The ending is discriminated by
    /// <see cref="SiteLogFrame.EndReason"/> rather than by the line, because an empty line is a
    /// legitimate log line and the end message may be absent.
    /// </remarks>
    private static string Format(SiteLogFrame frame)
    {
        if (frame.EndReason is null)
        {
            return EventStreamFrame.Render("line", new SiteLogLineDto(frame.Line, frame.Historical));
        }

        return EventStreamFrame.Render("end", new SiteLogEndDto(frame.EndReason.Value, frame.EndMessage));
    }
}
