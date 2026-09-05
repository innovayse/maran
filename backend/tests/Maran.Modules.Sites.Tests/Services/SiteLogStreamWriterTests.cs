using System.Runtime.CompilerServices;
using Maran.Modules.Sites.Domain.Enums;
using Maran.Modules.Sites.Models;
using Maran.Modules.Sites.Options;
using Maran.Modules.Sites.Services;
using Maran.Modules.Sites.Tests.TestSupport;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Maran.Modules.Sites.Tests.Services;

/// <summary>Behavioral contract of <see cref="SiteLogStreamWriter"/>: the bytes on the wire.</summary>
/// <remarks>
/// The exact bytes, not a parsed reading of them. A server-sent event is dispatched by a browser
/// only when it is terminated by a BLANK line, so a frame that a lenient reader parses happily can
/// still be a frame no browser ever delivers — the suite's own stream reader has that blind spot,
/// and these tests are what closes it. The second property here is that a log line is a customer's
/// own file content: a line containing a newline must not be able to end its frame early and forge
/// events of its own.
/// </remarks>
public sealed class SiteLogStreamWriterTests
{
    /// <summary>A line frame is written as event, data and the blank line that terminates it.</summary>
    [Fact]
    public async Task A_line_frame_is_written_as_event_data_and_the_blank_line_that_terminates_it()
    {
        var written = await WriteAsync(SiteLogFrame.OfLine("hello", historical: true));

        Assert.Equal(
            ": open\n\nevent: line\ndata: {\"line\":\"hello\",\"historical\":true}\n\n",
            written);
    }

    /// <summary>An end frame is written with its reason and no message when there is none.</summary>
    [Fact]
    public async Task An_end_frame_is_written_with_its_reason_and_no_message_when_there_is_none()
    {
        var written = await WriteAsync(SiteLogFrame.OfEnd(SiteLogEndReason.Dropped, null));

        Assert.Equal(": open\n\nevent: end\ndata: {\"reason\":\"dropped\"}\n\n", written);
    }

    /// <summary>An end frame carries its localized sentence when there is one.</summary>
    [Fact]
    public async Task An_end_frame_carries_its_localized_sentence_when_there_is_one()
    {
        var written = await WriteAsync(SiteLogFrame.OfEnd(SiteLogEndReason.Failed, "it broke"));

        Assert.Equal(": open\n\nevent: end\ndata: {\"reason\":\"failed\",\"message\":\"it broke\"}\n\n", written);
    }

    /// <summary>A log line cannot forge a frame with newlines or quotes of its own.</summary>
    [Fact]
    public async Task A_log_line_cannot_forge_a_frame_with_newlines_or_quotes_of_its_own()
    {
        // The line is the customer's own file. Written by concatenation instead of a serializer,
        // this content would close its own frame and inject a second, attacker-chosen event into
        // the operator's pane.
        var hostile = "a\n\nevent: end\ndata: {\"reason\":\"completed\"}\n\nb\r\"quoted\"";

        var written = await WriteAsync(SiteLogFrame.OfLine(hostile, historical: false));

        var frames = written.Split("\n\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, frames.Length);
        Assert.Equal(": open", frames[0]);
        Assert.Equal(
            "event: line\ndata: {\"line\":\"a\\n\\nevent: end\\ndata: {\\u0022reason\\u0022:\\u0022completed\\u0022}"
            + "\\n\\nb\\r\\u0022quoted\\u0022\",\"historical\":false}",
            frames[1]);
    }

    /// <summary>The stream announces itself before the first log line arrives.</summary>
    [Fact]
    public async Task The_stream_announces_itself_before_the_first_log_line_arrives()
    {
        // Response headers do not flush until the first body write, so without this a pane on a
        // quiet log cannot tell "connecting" from "broken" — it sees nothing either way.
        var written = await WriteAsync();

        Assert.Equal(": open\n\n", written);
    }

    /// <summary>A stream that produces nothing for the heartbeat interval keeps itself alive.</summary>
    [Fact]
    public async Task A_stream_that_produces_nothing_for_the_heartbeat_interval_keeps_itself_alive()
    {
        // The comment frame is the whole defence against a proxy read timeout tearing the stream
        // down with no end event — the silent truncation the six reasons exist to prevent.
        var response = NewResponse(out var body);
        using var stop = new CancellationTokenSource();


        var writing = Writer(heartbeatSeconds: 1).WriteAsync(response, NoFramesAsync(stop.Token), stop.Token);
        await WaitForAsync(() =>
        {
            return Read(body).Contains(": keepalive", StringComparison.Ordinal);
        });

        await stop.CancelAsync();
        await writing;

        Assert.StartsWith(": open\n\n: keepalive\n\n", Read(body), StringComparison.Ordinal);
    }

    /// <summary>A write that fails while a frame is still pending stops the read rather than abandoning it.</summary>
    [Fact]
    public async Task A_write_that_fails_while_a_frame_is_still_pending_stops_the_read_rather_than_abandoning_it()
    {
        // The heartbeat write is the one that can fail while the NEXT frame is still in flight,
        // and walking away from it there is not merely a leak. Disposing an async enumerator with
        // a MoveNextAsync outstanding resets its value-task source under the pending await and
        // throws on a thread pool thread — an unhandled exception that takes the api process down.
        // This is the shape that crashed the test host before the writer drained what it started.
        var context = new DefaultHttpContext();
        context.Response.Body = new FailingResponseStream(allowedWrites: 1);
        var frames = new CancellationObservingFrameSource();

        // Bounded by WaitAsync rather than by a token: the writer here is meant to stop because
        // its heartbeat write FAILS, so a token that ended it would test the wrong thing, and no
        // token at all would let a defect hang the run instead of failing it.
        var writing = Writer(heartbeatSeconds: 1)
            .WriteAsync(context.Response, frames.ReadAsync(default), CancellationToken.None);

        await writing.WaitAsync(TimeSpan.FromSeconds(15));

        Assert.True(frames.Stopped);
    }

    /// <summary>A write that fails because the client has gone stops the stream being read.</summary>
    [Fact]
    public async Task A_write_that_fails_because_the_client_has_gone_stops_the_stream_being_read()
    {
        // Otherwise the panel keeps pulling a log nobody is watching, for as long as the site keeps
        // writing to it. The source is bounded so that a consumer which fails to stop shows up as a
        // count, not as a run that never finishes.
        var context = new DefaultHttpContext();
        context.Response.Body = new FailingResponseStream(allowedWrites: 1);
        var frames = new CountingFrameSource();

        await Writer().WriteAsync(context.Response, frames.ReadAsync(), CancellationToken.None);

        Assert.Equal(1, frames.Yielded);
    }

    /// <summary>Writes the given frames to a fresh response and returns what reached the wire.</summary>
    /// <param name="frames">The frames to write.</param>
    /// <returns>The response body as text.</returns>
    private static async Task<string> WriteAsync(params SiteLogFrame[] frames)
    {
        var response = NewResponse(out var body);

        await Writer().WriteAsync(response, Enumerate(frames), CancellationToken.None);

        return Read(body);
    }

    /// <summary>Builds the writer under test.</summary>
    /// <param name="heartbeatSeconds">The heartbeat interval; long enough to never fire by default.</param>
    /// <returns>The writer.</returns>
    private static SiteLogStreamWriter Writer(int heartbeatSeconds = 600)
    {
        return new SiteLogStreamWriter(
            new OptionsWrapper<SiteLogOptions>(new SiteLogOptions { HeartbeatSeconds = heartbeatSeconds }));
    }

    /// <summary>Builds a response whose body is a memory stream the test can read back.</summary>
    /// <param name="body">The stream the response writes into.</param>
    /// <returns>The response.</returns>
    private static HttpResponse NewResponse(out RecordingResponseStream body)
    {
        var context = new DefaultHttpContext();
        body = new RecordingResponseStream();
        context.Response.Body = body;
        return context.Response;
    }

    /// <summary>Reads everything written so far, safely while writing continues.</summary>
    /// <param name="body">The stream to read.</param>
    /// <returns>Everything written so far.</returns>
    private static string Read(RecordingResponseStream body)
    {
        return body.Text;
    }

    /// <summary>Produces no frames at all, ending only when the caller stops watching.</summary>
    /// <param name="cancellationToken">Cancelled when the caller stops watching.</param>
    /// <returns>An empty sequence that never completes on its own — a site with no traffic.</returns>
    private static async IAsyncEnumerable<SiteLogFrame> NoFramesAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.Delay(Timeout.Infinite, cancellationToken);
        yield break;
    }

    /// <summary>Yields a fixed set of frames.</summary>
    /// <param name="frames">The frames to yield.</param>
    /// <returns>The frames, as an asynchronous sequence.</returns>
    private static async IAsyncEnumerable<SiteLogFrame> Enumerate(SiteLogFrame[] frames)
    {
        foreach (var frame in frames)
        {
            yield return frame;
        }

        await Task.CompletedTask;
    }

    /// <summary>Polls a condition until it holds, or fails the test at its bound.</summary>
    /// <param name="condition">What must become true.</param>
    private static async Task WaitForAsync(Func<bool> condition)
    {
        const int PollIntervalMilliseconds = 20;
        const int Polls = 500;

        for (var poll = 0; poll < Polls; poll++)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(PollIntervalMilliseconds));
        }

        Assert.Fail("Timed out waiting for the stream to write a heartbeat.");
    }
}
