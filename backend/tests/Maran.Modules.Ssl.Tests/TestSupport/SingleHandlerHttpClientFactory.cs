namespace Maran.Modules.Ssl.Tests.TestSupport;

/// <summary>An <see cref="IHttpClientFactory"/> handing out one client over one handler, as the production registration would.</summary>
public sealed class SingleHandlerHttpClientFactory : IHttpClientFactory
{
    /// <summary>The handler every client is built over.</summary>
    private readonly HttpMessageHandler _handler;

    /// <summary>Creates the factory over one handler.</summary>
    /// <param name="handler">The handler every client is built over.</param>
    public SingleHandlerHttpClientFactory(HttpMessageHandler handler)
    {
        _handler = handler;
    }

    /// <inheritdoc />
    public HttpClient CreateClient(string name)
    {
        var client = new HttpClient(_handler, disposeHandler: false) { Timeout = Timeout.InfiniteTimeSpan };

        // Mirrors the real registration these tests do not go through (Maran.Modules.Ssl's
        // SslModule.AddHttpClient(AcmeOptions.HttpClientName, ...)): pebble refuses every request
        // with no User-Agent as "malformed", which is the gap the first Pebble test surfaced.
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Maran-Panel-Acme-Client/1.0");
        return client;
    }
}
