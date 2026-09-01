namespace Maran.Modules.Ssl.Tests.TestSupport;

/// <summary>
/// An <see cref="IHttpClientFactory"/> that hands out one client over a caller-supplied handler.
/// </summary>
/// <remarks>
/// The name is asserted rather than ignored: the whole reason the ACME client asks the factory for a
/// NAMED client is that the Host attaches the resilience pipeline to that name, so a client built
/// with the wrong name — or with <c>new HttpClient()</c> — would be ungoverned and no assertion on
/// the order's outcome would notice.
/// </remarks>
public sealed class StubHttpClientFactory : IHttpClientFactory
{
    /// <summary>The handler every client this factory creates is built over.</summary>
    private readonly HttpMessageHandler _handler;

    /// <summary>The names this factory was asked for, in order.</summary>
    public List<string> RequestedNames { get; } = [];

    /// <summary>Creates the factory over one handler.</summary>
    /// <param name="handler">The handler every client is built over.</param>
    public StubHttpClientFactory(HttpMessageHandler handler)
    {
        _handler = handler;
    }

    /// <inheritdoc />
    public HttpClient CreateClient(string name)
    {
        RequestedNames.Add(name);

        // disposeHandler: false — the ACME client disposes the HttpClient it obtains, and a test
        // asserting on the handler afterwards must not be reading a disposed object.
        return new HttpClient(_handler, disposeHandler: false) { Timeout = Timeout.InfiniteTimeSpan };
    }
}
