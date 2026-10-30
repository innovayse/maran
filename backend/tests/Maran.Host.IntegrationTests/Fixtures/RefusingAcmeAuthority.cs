using System.Net;
using System.Text;

namespace Maran.Host.IntegrationTests.Fixtures;

/// <summary>
/// A certificate authority that publishes its directory, hands out nonces, and refuses every signed
/// request with an <c>invalidContact</c> problem document — the refusal the panel met in a real run.
/// </summary>
/// <remarks>
/// It stands in as the primary handler of the panel's named ACME client, so everything above the
/// socket is the real panel: the directory read, the nonce fetch, the signed registration, the
/// classification of the refusal and its translation to a status. Only the authority is replaced,
/// because it is the one party a test cannot run.
/// </remarks>
public sealed class RefusingAcmeAuthority : HttpMessageHandler
{
    /// <summary>The authority's base address.</summary>
    public const string BaseUrl = "https://acme.test";

    /// <summary>The directory URL the panel is configured with.</summary>
    public const string DirectoryUrl = BaseUrl + "/directory";

    /// <summary>Answers one request.</summary>
    /// <param name="request">The request the panel sent.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>The directory, a nonce, or the refusal.</returns>
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var url = request.RequestUri!.GetLeftPart(UriPartial.Path);

        if (request.Method == HttpMethod.Get && url == DirectoryUrl)
        {
            return Task.FromResult(WithNonce(Body(
                HttpStatusCode.OK,
                "application/json",
                "{\"newNonce\":\"" + BaseUrl + "/new-nonce\","
                + "\"newAccount\":\"" + BaseUrl + "/new-acct\","
                + "\"newOrder\":\"" + BaseUrl + "/new-order\"}")));
        }

        if (request.Method == HttpMethod.Head || request.Method == HttpMethod.Get)
        {
            return Task.FromResult(WithNonce(new HttpResponseMessage(HttpStatusCode.OK)));
        }

        return Task.FromResult(WithNonce(Body(
            HttpStatusCode.BadRequest,
            "application/problem+json",
            "{\"type\":\"urn:ietf:params:acme:error:invalidContact\",\"detail\":\"contact refused\"}")));
    }

    /// <summary>Builds a response carrying a text body of the given media type.</summary>
    /// <param name="status">The status to answer with.</param>
    /// <param name="mediaType">The body's media type.</param>
    /// <param name="content">The body.</param>
    /// <returns>The response.</returns>
    private static HttpResponseMessage Body(HttpStatusCode status, string mediaType, string content)
    {
        return new HttpResponseMessage(status)
        {
            Content = new StringContent(content, Encoding.UTF8, mediaType),
        };
    }

    /// <summary>Attaches a fresh nonce, as every ACME response does.</summary>
    /// <param name="response">The response to stamp.</param>
    /// <returns>The same response.</returns>
    private static HttpResponseMessage WithNonce(HttpResponseMessage response)
    {
        response.Headers.Add("Replay-Nonce", Guid.NewGuid().ToString("N"));
        return response;
    }
}
