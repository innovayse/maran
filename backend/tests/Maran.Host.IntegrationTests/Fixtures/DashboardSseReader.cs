using System.Net;
using System.Text.Json;

namespace Maran.Host.IntegrationTests.Fixtures;

/// <summary>
/// Reads the dashboard stream's events off one open response, keeping what it has read across calls.
/// </summary>
/// <remarks>
/// One reader per response and not one per call: a <see cref="StreamReader"/> reads ahead of the line it
/// returns, so a second reader over the same body would lose whatever the first had already buffered. The
/// events accumulate here for the same reason, which lets a test read to one point, change something,
/// and read on.
/// </remarks>
public sealed class DashboardSseReader : IDisposable
{
    /// <summary>The body, read line by line.</summary>
    private readonly StreamReader _reader;

    /// <summary>The events read so far, in order.</summary>
    private readonly List<DashboardSseEvent> _events = [];

    /// <summary>The name from the most recent <c>event:</c> line, which the next <c>data:</c> line belongs to.</summary>
    private string _pendingName = string.Empty;

    /// <summary>Creates a reader over a body.</summary>
    /// <param name="body">The response body.</param>
    private DashboardSseReader(Stream body)
    {
        _reader = new StreamReader(body);
    }

    /// <summary>Opens a reader over a response whose headers have been read.</summary>
    /// <param name="response">An open stream; asserted to be a successful event stream.</param>
    /// <param name="cancellationToken">The read deadline.</param>
    /// <returns>The reader.</returns>
    public static async Task<DashboardSseReader> OpenAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);

        return new DashboardSseReader(await response.Content.ReadAsStreamAsync(cancellationToken));
    }

    /// <summary>Reads until <paramref name="done"/> accepts the events read so far, or the stream ends.</summary>
    /// <param name="done">Judges the events read so far; true stops the read.</param>
    /// <param name="cancellationToken">The read deadline.</param>
    /// <returns>Every event read since the reader was opened, in order.</returns>
    public async Task<IReadOnlyList<DashboardSseEvent>> ReadUntilAsync(
        Func<IReadOnlyList<DashboardSseEvent>, bool> done,
        CancellationToken cancellationToken)
    {
        while (!done(_events))
        {
            var line = await _reader.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                return _events;
            }

            if (line.StartsWith("event: ", StringComparison.Ordinal))
            {
                _pendingName = line["event: ".Length..];
            }
            else if (line.StartsWith("data: ", StringComparison.Ordinal))
            {
                using var document = JsonDocument.Parse(line["data: ".Length..]);
                _events.Add(new DashboardSseEvent(_pendingName, document.RootElement.Clone()));
            }
        }

        return _events;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _reader.Dispose();
    }
}
