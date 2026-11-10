using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Maran.Modules.Ssl.Services;

/// <summary>Fetches the authority's directory document — the one ACME call that needs no signature.</summary>
internal static class AcmeDirectoryReader
{
    /// <summary>Fetches and parses the directory document.</summary>
    /// <param name="http">The named ACME client.</param>
    /// <param name="url">The document's URL.</param>
    /// <param name="logger">Where a retried attempt is noted.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>The parsed document, or <c>null</c> when the authority did not answer with one.</returns>
    /// <remarks>
    /// This one is retried, unlike every signed request: it is an unsigned GET with no side effect at
    /// the authority, so a replay costs nothing and cannot duplicate an order (see
    /// <see cref="AcmeTransport"/>).
    /// </remarks>
    public static async Task<JsonElement?> ReadAsync(
        HttpClient http,
        string url,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        using var response = await AcmeTransport.SendIdempotentAsync(
            http,
            url,
            () =>
            {
                return new HttpRequestMessage(HttpMethod.Get, new Uri(url));
            },
            logger,
            cancellationToken);
        if (response is null || !response.IsSuccessStatusCode)
        {
            return null;
        }

        try
        {
            return await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        }
        catch (JsonException)
        {
            // An authority answering its directory with something that is not JSON is unreachable for
            // this client's purposes; the caller turns null into AcmeAuthorityUnreachable.
            return null;
        }
    }
}
