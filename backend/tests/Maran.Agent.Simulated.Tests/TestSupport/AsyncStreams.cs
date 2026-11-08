namespace Maran.Agent.Simulated.Tests.TestSupport;

/// <summary>Reads a whole asynchronous stream, so a test can assert on every event it produced.</summary>
public static class AsyncStreams
{
    /// <summary>Reads <paramref name="stream"/> to its end.</summary>
    /// <typeparam name="T">The event type.</typeparam>
    /// <param name="stream">The stream to read.</param>
    /// <returns>Every event, in the order the stream produced them.</returns>
    public static async Task<List<T>> CollectAsync<T>(IAsyncEnumerable<T> stream)
    {
        var events = new List<T>();
        await foreach (var item in stream)
        {
            events.Add(item);
        }

        return events;
    }
}
