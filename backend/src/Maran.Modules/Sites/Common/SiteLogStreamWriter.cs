using System.Text.Json;
using System.Text.Json.Serialization;
using Maran.Modules.Sites.Common.Options;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;

namespace Maran.Modules.Sites.Common;

/// <summary>
/// Writes a sequence of <see cref="SiteLogFrame"/> to a client as server-sent events: one
/// <c>line</c> event per log line, one final <c>end</c> event naming why the stream stopped, and a
/// comment frame whenever neither has been sent for a while.
/// </summary>
/// <remarks>
/// Server-sent events rather than a WebSocket because the traffic is one-way and the panel already
/// authenticates every request with a bearer token and a CSRF header — a plain HTTP response carries
/// both, where the browser's <c>EventSource</c> can carry neither.
///
/// <para>The heartbeat is not cosmetic.</para> Two failures live in the gap between frames. A proxy
/// in front of the panel closes an upstream connection that has been silent for its read timeout —
/// nginx's default is 60 seconds, five times shorter than the agent's own 300-second idle guard — so
/// without a heartbeat the common case, a site with no traffic, has its stream torn down every
/// minute with NO <c>end</c> event at all, and the browser reports a truncation on a perfectly
/// healthy log. And until the first byte is written the response headers do not flush, so a pane on
/// a quiet log cannot tell "connecting" from "broken"; the frame written immediately after the
/// headers is what settles that. A comment (<c>: text</c>) is the SSE format's own no-op: it keeps
/// the connection warm and dispatches no event.
///
/// <para>Buffering.</para> <c>DisableBuffering()</c> and the explicit flush after each frame are two
/// spellings of "push this byte now", and they mask each other: on Kestrel, disabling buffering makes
/// the output producer flush every write, and <c>WriteAsync</c> already flushes its pipe, so either
/// one alone still delivers incrementally. Removing both does not. They are kept as a pair, and this
/// note is here because no test in this repository can tell them apart — the suite runs on
/// <c>TestServer</c>, which has neither Kestrel's output buffering nor a proxy in front of it. The
/// thing that actually breaks incremental delivery on a shipped server is the proxy read timeout
/// above, and that is what the heartbeat and the installer's vhost address.
///
/// <para>Reconnection.</para> No <c>id:</c> field is written and <c>Last-Event-ID</c> is not read.
/// That is deliberate rather than forgotten: resuming a tail would mean the panel remembering a
/// position in a file it does not own and cannot seek reliably, and the SPA reads this endpoint with
/// <c>fetch</c> rather than <c>EventSource</c>, so nothing reconnects on its own. A client that
/// reopens a tail gets a fresh history replay, which is honest about what it is.
///
/// A write that fails because the client has gone stops the loop rather than throwing. The client
/// disappearing is the ordinary way a tail ends, not an error, and the caller's cancellation has
/// already stopped the agent's side of it.
/// </remarks>
public sealed class SiteLogStreamWriter
{
    /// <summary>The media type of a server-sent event stream.</summary>
    public const string EventStreamContentType = "text/event-stream";

    /// <summary>The comment frame written as soon as the stream opens, before any log line.</summary>
    private const string OpenFrame = ": open\n\n";

    /// <summary>The comment frame written when nothing else has been sent for the heartbeat interval.</summary>
    private const string HeartbeatFrame = ": keepalive\n\n";

    /// <summary>How the JSON payload of each event is written: camel case, with reasons as their names.</summary>
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>How long the stream may be silent before a heartbeat is written.</summary>
    private readonly TimeSpan _heartbeatInterval;

    /// <summary>Creates the writer.</summary>
    /// <param name="options">The stream's settings, chiefly the heartbeat interval.</param>
    public SiteLogStreamWriter(IOptions<SiteLogOptions> options)
    {
        _heartbeatInterval = TimeSpan.FromSeconds(options.Value.HeartbeatSeconds);
    }

    /// <summary>Streams every frame to the response, flushing each one, heartbeating between them.</summary>
    /// <param name="response">The response to write to; its headers are set here and not before.</param>
    /// <param name="frames">The frames to write, ending with exactly one terminal frame.</param>
    /// <param name="cancellationToken">Cancelled when the watching client goes away.</param>
    public async Task WriteAsync(
        HttpResponse response,
        IAsyncEnumerable<SiteLogFrame> frames,
        CancellationToken cancellationToken)
    {
        PrepareResponse(response);
        if (!await TryWriteAsync(response, OpenFrame, cancellationToken))
        {
            return;
        }

        // The enumeration gets its own cancellation source so that leaving this loop early — the
        // client went away mid-write — can STOP the frame source before the enumerator is disposed.
        // Disposing an async enumerator while one of its MoveNextAsync calls is still outstanding is
        // not merely untidy: the state machine's value-task source is reset under the pending await
        // and throws InvalidOperationException on a thread pool thread, which takes the whole
        // process down. That is not a theoretical ordering — it is the crash this writer produced,
        // and the reason the loop below never returns without draining what it started.
        using var readStopped = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        await using var enumerator = frames.GetAsyncEnumerator(readStopped.Token);
        Task<bool>? pending = null;

        try
        {
            while (true)
            {
                // AsTask, because a ValueTask may be awaited only once and this one is handed to
                // Task.WhenAny repeatedly while the heartbeats go out underneath it.
                pending = enumerator.MoveNextAsync().AsTask();
                if (!await AwaitFrameAsync(response, pending, cancellationToken))
                {
                    return;
                }

                var arrived = await pending;
                pending = null;
                if (!arrived)
                {
                    return;
                }

                if (!await TryWriteAsync(response, Format(enumerator.Current), cancellationToken))
                {
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // The ordinary end of a tail: the client closed the pane. Not a fault to surface through
            // the exception middleware, on a response whose status was fixed when its headers went out.
        }
        finally
        {
            await DrainAsync(readStopped, pending);
        }
    }

    /// <summary>Stops the frame source and waits for any move it still has in flight.</summary>
    /// <param name="readStopped">The enumeration's own cancellation source.</param>
    /// <param name="pending">The move still in flight, or <c>null</c> when there is none.</param>
    /// <remarks>
    /// Only cancellation is swallowed here, and only because cancelling is what this method just did.
    /// Any other failure from the frame source is a real defect and is left to surface.
    /// </remarks>
    private static async Task DrainAsync(CancellationTokenSource readStopped, Task<bool>? pending)
    {
        if (pending is null)
        {
            return;
        }

        await readStopped.CancelAsync();
        try
        {
            await pending;
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>Waits for the next frame, writing a heartbeat every interval it does not arrive in.</summary>
    /// <param name="response">The response to heartbeat on.</param>
    /// <param name="pending">The in-flight move to the next frame.</param>
    /// <param name="cancellationToken">Cancelled when the watching client goes away.</param>
    /// <returns><c>true</c> when the frame arrived; <c>false</c> when the client has gone.</returns>
    private async Task<bool> AwaitFrameAsync(
        HttpResponse response,
        Task<bool> pending,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            // A fresh linked source per wait, cancelled in the finally, so a stream that runs for
            // hours does not accumulate one live timer per heartbeat.
            using var heartbeatWait = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var timer = Task.Delay(_heartbeatInterval, heartbeatWait.Token);
            try
            {
                if (await Task.WhenAny(pending, timer) == pending)
                {
                    return true;
                }
            }
            finally
            {
                await heartbeatWait.CancelAsync();
            }

            if (!await TryWriteAsync(response, HeartbeatFrame, cancellationToken))
            {
                return false;
            }
        }
    }

    /// <summary>Puts the response into streaming mode before the first byte is written.</summary>
    /// <param name="response">The response to prepare.</param>
    private static void PrepareResponse(HttpResponse response)
    {
        response.ContentType = EventStreamContentType;
        response.Headers.CacheControl = "no-cache";

        // Read by nginx in front of the panel, which otherwise buffers a proxied response and
        // undoes the flushing below.
        response.Headers["X-Accel-Buffering"] = "no";
        response.HttpContext.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();
    }

    /// <summary>Writes one already-rendered frame, reporting whether the client was still there.</summary>
    /// <param name="response">The response to write to.</param>
    /// <param name="frame">The frame's wire text.</param>
    /// <param name="cancellationToken">Cancelled when the watching client goes away.</param>
    /// <returns><c>true</c> when the frame was written and flushed; <c>false</c> when the client has gone.</returns>
    private static async Task<bool> TryWriteAsync(
        HttpResponse response,
        string frame,
        CancellationToken cancellationToken)
    {
        try
        {
            await response.WriteAsync(frame, cancellationToken);
            await response.Body.FlushAsync(cancellationToken);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (IOException)
        {
            // The connection was torn down under us. Nothing to report to a client that is gone,
            // and nothing left to clean up: the agent's stream stops with this enumeration.
            return false;
        }
    }

    /// <summary>Renders one frame as a server-sent event.</summary>
    /// <param name="frame">The frame to render.</param>
    /// <returns>The event's wire text, terminated by the blank line that ends an event.</returns>
    /// <remarks>
    /// The payload is built by <see cref="JsonSerializer"/> and never by concatenation, and that is
    /// a safety property rather than a style preference: a log line is a customer's own file content,
    /// so a line containing a newline would otherwise close the frame early and let the customer's
    /// log forge events of its own. JSON escaping keeps every line inside its own frame, and the
    /// trailing blank line is what makes a browser dispatch the event at all.
    /// </remarks>
    private static string Format(SiteLogFrame frame)
    {
        if (frame.EndReason is null)
        {
            var line = JsonSerializer.Serialize(new SiteLogLineDto(frame.Line, frame.Historical), SerializerOptions);
            return $"event: line\ndata: {line}\n\n";
        }

        var end = JsonSerializer.Serialize(
            new SiteLogEndDto(frame.EndReason.Value, frame.EndMessage), SerializerOptions);
        return $"event: end\ndata: {end}\n\n";
    }
}
