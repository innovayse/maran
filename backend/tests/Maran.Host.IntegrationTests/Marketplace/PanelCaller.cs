using System.Net.Http.Json;

namespace Maran.Host.IntegrationTests.Marketplace;

/// <summary>
/// A signed-in client that remembers every answer body it was given, so a test can search everything the
/// panel said during a session - not only the answer it happens to assert on.
/// </summary>
public sealed class PanelCaller : IDisposable
{
    /// <summary>The signed-in client.</summary>
    private readonly HttpClient _client;

    /// <summary>The bodies of every answer so far, in order.</summary>
    private readonly List<string> _texts = [];

    /// <summary>The bodies of every answer so far, in order.</summary>
    public IReadOnlyList<string> Texts
    {
        get
        {
            return _texts;
        }
    }

    /// <summary>Wraps a client.</summary>
    /// <param name="client">The signed-in client; this caller disposes it.</param>
    public PanelCaller(HttpClient client)
    {
        _client = client;
    }

    /// <summary>Sends one call.</summary>
    /// <param name="method">The HTTP verb.</param>
    /// <param name="path">The absolute path, with any query.</param>
    /// <param name="body">The JSON body, or null for none.</param>
    /// <returns>The status and the body text.</returns>
    public async Task<PanelAnswer> SendAsync(HttpMethod method, string path, object? body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        using var response = await _client.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        _texts.Add(text);

        return new PanelAnswer(response.StatusCode, text);
    }

    /// <summary>Sends a GET.</summary>
    /// <param name="path">The absolute path, with any query.</param>
    /// <returns>The status and the body text.</returns>
    public Task<PanelAnswer> GetAsync(string path)
    {
        return SendAsync(HttpMethod.Get, path);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _client.Dispose();
    }
}
