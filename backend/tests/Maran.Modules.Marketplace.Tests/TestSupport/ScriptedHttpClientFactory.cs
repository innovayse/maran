namespace Maran.Modules.Marketplace.Tests.TestSupport;

/// <summary>
/// An <see cref="IHttpClientFactory"/> that hands out clients over one scripted handler and counts how
/// often it was asked. A disabled marketplace must never ask, and this count is what shows it.
/// </summary>
public sealed class ScriptedHttpClientFactory : IHttpClientFactory
{
    /// <summary>The handler every client sends through.</summary>
    private readonly HttpMessageHandler _handler;

    /// <summary>Creates the factory.</summary>
    /// <param name="handler">The handler every client sends through.</param>
    public ScriptedHttpClientFactory(HttpMessageHandler handler)
    {
        _handler = handler;
    }

    /// <summary>How many clients were created.</summary>
    public int CreateCount { get; private set; }

    /// <inheritdoc />
    public HttpClient CreateClient(string name)
    {
        CreateCount++;
        return new HttpClient(_handler, disposeHandler: false) { Timeout = Timeout.InfiniteTimeSpan };
    }
}
