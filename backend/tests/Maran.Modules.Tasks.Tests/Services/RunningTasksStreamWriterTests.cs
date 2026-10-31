using Maran.Modules.Tasks.Models;
using Maran.Modules.Tasks.Options;
using Maran.Modules.Tasks.Services;
using Maran.Modules.Tasks.Tests.TestSupport;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Maran.Modules.Tasks.Tests.Services;

/// <summary>The bytes <see cref="RunningTasksStreamWriter"/> puts on the wire for a running set.</summary>
/// <remarks>
/// The task frame is the same <c>task</c> event the per-task writer renders, through the same
/// <c>EventStreamFrame</c>, and <c>TaskStreamWriterTests</c> pins its bytes; what is new here is the
/// <c>running</c> event, and that is what this pins.
/// </remarks>
public sealed class RunningTasksStreamWriterTests
{
    /// <summary>A running set is written as a running event naming the ids, terminated by a blank line.</summary>
    [Fact]
    public async Task A_running_set_is_written_as_a_running_event_naming_the_ids()
    {
        var context = new DefaultHttpContext();
        var body = new RecordingResponseStream();
        context.Response.Body = body;
        var writer = new RunningTasksStreamWriter(
            new OptionsWrapper<TaskStreamOptions>(new TaskStreamOptions { HeartbeatSeconds = 30 }));

        await writer.WriteAsync(
            context.Response,
            Enumerate(RunningTasksFrame.OfRunning([Guid.Parse("11111111-2222-3333-4444-555555555555")])),
            CancellationToken.None);

        Assert.Equal(
            ": open\n\nevent: running\ndata: {\"ids\":[\"11111111-2222-3333-4444-555555555555\"]}\n\n",
            body.Text);
    }

    /// <summary>Yields a fixed set of frames.</summary>
    /// <param name="frames">The frames to yield.</param>
    /// <returns>The frames, as an asynchronous sequence.</returns>
    private static async IAsyncEnumerable<RunningTasksFrame> Enumerate(params RunningTasksFrame[] frames)
    {
        foreach (var frame in frames)
        {
            yield return frame;
        }

        await Task.CompletedTask;
    }
}
