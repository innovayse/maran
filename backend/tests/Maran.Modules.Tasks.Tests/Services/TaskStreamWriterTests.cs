using Maran.Modules.Tasks.Common;
using Maran.Modules.Tasks.Domain.Enums;
using Maran.Modules.Tasks.Models;
using Maran.Modules.Tasks.Options;
using Maran.Modules.Tasks.Services;
using Maran.Modules.Tasks.Tests.TestSupport;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Maran.Modules.Tasks.Tests.Services;

/// <summary>Behavioral contract of <see cref="TaskStreamWriter"/>: the bytes on the wire.</summary>
/// <remarks>
/// The exact bytes, not a parsed reading of them. A server-sent event is dispatched by a browser
/// only when it is terminated by a BLANK line, so a frame a lenient reader parses happily can still
/// be a frame no browser ever delivers. These bytes are also the ones the SPA's existing stream
/// helper already parses for site logs, so a drift here is a task pane that silently shows nothing
/// while every test that reads the objects instead of the text stays green.
/// </remarks>
public sealed class TaskStreamWriterTests
{
    /// <summary>The task every frame in these tests carries.</summary>
    private static readonly PanelTaskDto Snapshot = new(
        Guid.Parse("11111111-2222-3333-4444-555555555555"),
        "CertificateIssue",
        "example.com",
        null,
        PanelTaskStatus.Running,
        40,
        "ordering",
        null,
        new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero),
        null,
        1);

    /// <summary>A task frame is written as event data and the blank line that terminates it.</summary>
    [Fact]
    public async Task A_task_frame_is_written_as_event_data_and_the_blank_line_that_terminates_it()
    {
        var written = await WriteAsync(TaskFrame.OfTask(Snapshot));

        Assert.Equal(
            ": open\n\nevent: task\ndata: {\"id\":\"11111111-2222-3333-4444-555555555555\","
            + "\"kind\":\"CertificateIssue\",\"subject\":\"example.com\",\"status\":\"running\","
            + "\"percent\":40,\"log\":\"ordering\","
            + "\"startedAt\":\"2026-03-01T12:00:00+00:00\",\"revision\":1}\n\n",
            written);
    }

    /// <summary>An end frame names the status the task reached.</summary>
    [Fact]
    public async Task An_end_frame_names_the_status_the_task_reached()
    {
        var written = await WriteAsync(TaskFrame.OfEnd(PanelTaskStatus.Failed));

        Assert.Equal(": open\n\nevent: end\ndata: {\"status\":\"failed\"}\n\n", written);
    }

    /// <summary>A task log cannot forge a frame with newlines or quotes of its own.</summary>
    [Fact]
    public async Task A_task_log_cannot_forge_a_frame_with_newlines_or_quotes_of_its_own()
    {
        // A task's log is whatever the instrumented operation reported and its subject is a name a
        // caller chose. Written by concatenation instead of a serializer, this content would close
        // its own frame and inject a second, chosen event into the operator's pane.
        var hostile = Snapshot with { Log = "a\n\nevent: end\ndata: {\"status\":\"completed\"}\n\nb\r\"quoted\"" };

        var written = await WriteAsync(TaskFrame.OfTask(hostile));

        var frames = written.Split("\n\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, frames.Length);
        Assert.Equal(": open", frames[0]);
        Assert.StartsWith("event: task\ndata: {", frames[1], StringComparison.Ordinal);

        // The hostile text survives only in its ESCAPED form, which is the whole property: the
        // characters "event: end" are still in there, and they are inert because the newlines that
        // would have started a line of their own are two-character escapes now. Asserted as the
        // frame's line count, because that is exactly what a browser's parser counts.
        Assert.Contains("\\n\\nevent: end", frames[1], StringComparison.Ordinal);
        Assert.Equal(2, frames[1].Split('\n').Length);
    }

    /// <summary>The stream announces itself before the first update arrives.</summary>
    [Fact]
    public async Task The_stream_announces_itself_before_the_first_update_arrives()
    {
        // Response headers do not flush until the first body write, so without this a pane on a task
        // in a long silent stage cannot tell "connecting" from "broken" — it sees nothing either way.
        var written = await WriteAsync();

        Assert.Equal(": open\n\n", written);
    }

    /// <summary>A stream that produces nothing for the heartbeat interval keeps itself alive.</summary>
    [Fact]
    public async Task A_stream_that_produces_nothing_for_the_heartbeat_interval_keeps_itself_alive()
    {
        // The comment frame is the whole defence against a proxy read timeout tearing the stream
        // down mid-operation with no end event, leaving a pane frozen at a percentage the task left
        // behind minutes ago.
        var response = NewResponse(out var body);
        using var stop = new CancellationTokenSource();

        var writing = Writer(heartbeatSeconds: 1).WriteAsync(response, NoFramesAsync(stop.Token), stop.Token);
        await WaitForAsync(() =>
        {
            return body.Text.Contains(": keepalive", StringComparison.Ordinal);
        });

        await stop.CancelAsync();
        await writing;

        Assert.StartsWith(": open\n\n: keepalive\n\n", body.Text, StringComparison.Ordinal);
    }

    /// <summary>Writes the given frames to a fresh response and returns what reached the wire.</summary>
    /// <param name="frames">The frames to write.</param>
    /// <returns>The response body as text.</returns>
    private static async Task<string> WriteAsync(params TaskFrame[] frames)
    {
        var response = NewResponse(out var body);

        await Writer().WriteAsync(response, Enumerate(frames), CancellationToken.None);

        return body.Text;
    }

    /// <summary>Builds the writer under test.</summary>
    /// <param name="heartbeatSeconds">The heartbeat interval; long enough to never fire by default.</param>
    /// <returns>The writer.</returns>
    private static TaskStreamWriter Writer(int heartbeatSeconds = 30)
    {
        return new TaskStreamWriter(
            new OptionsWrapper<TaskStreamOptions>(new TaskStreamOptions { HeartbeatSeconds = heartbeatSeconds }));
    }

    /// <summary>Builds a response whose body is a stream the test can read back while it is written.</summary>
    /// <param name="body">The stream the response writes into.</param>
    /// <returns>The response.</returns>
    private static HttpResponse NewResponse(out RecordingResponseStream body)
    {
        var context = new DefaultHttpContext();
        body = new RecordingResponseStream();
        context.Response.Body = body;
        return context.Response;
    }

    /// <summary>Produces no frames at all, ending only when the caller stops watching.</summary>
    /// <param name="cancellationToken">Cancelled when the caller stops watching.</param>
    /// <returns>An empty sequence that never completes on its own — a task in a long silent stage.</returns>
    private static async IAsyncEnumerable<TaskFrame> NoFramesAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.Delay(Timeout.Infinite, cancellationToken);
        yield break;
    }

    /// <summary>Yields a fixed set of frames.</summary>
    /// <param name="frames">The frames to yield.</param>
    /// <returns>The frames, as an asynchronous sequence.</returns>
    private static async IAsyncEnumerable<TaskFrame> Enumerate(TaskFrame[] frames)
    {
        foreach (var frame in frames)
        {
            yield return frame;
        }

        await Task.CompletedTask;
    }

    /// <summary>Polls a condition until it holds, or fails the test at its bound.</summary>
    /// <param name="condition">What must become true.</param>
    /// <returns>Resolves when the condition holds.</returns>
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
