using System.Net;
using System.Text;
using System.Text.Json;

namespace Maran.Modules.Ssl.Tests.TestSupport;

/// <summary>
/// A stand-in certificate authority: an <see cref="HttpMessageHandler"/> that speaks enough of RFC
/// 8555 for a whole order to run, and records every request it was sent.
/// </summary>
/// <remarks>
/// This is how the ACME conversation is testable at all. Every other double in this suite replaces
/// <c>IAcmeClient</c> wholesale, which is right for a handler test and useless for the protocol
/// itself — and the protocol is where the defects were: an order already <c>ready</c> was walked
/// through validation anyway, and the resilience layer replayed signed bodies.
///
/// It answers by URL rather than by strict sequence, so a test can assert what the client did NOT do
/// (never fetched the challenge URL, never wrote a file) as easily as what it did.
/// </remarks>
public sealed class FakeAcmeAuthority : HttpMessageHandler
{
    /// <summary>The authority's base address; every URL below hangs off it.</summary>
    public const string BaseUrl = "https://acme.test";

    /// <summary>The directory document's URL, which is the one thing the client is configured with.</summary>
    public const string DirectoryUrl = BaseUrl + "/directory";

    /// <summary>A leaf certificate that <see cref="System.Security.Cryptography.X509Certificates.X509Certificate2"/> can parse.</summary>
    private readonly string _certificatePem;

    /// <summary>Whether the challenge has been accepted, after which the authorization is valid.</summary>
    private bool _challengeAccepted;

    /// <summary>Every request this authority received, in order, as "METHOD url".</summary>
    public List<string> Requests { get; } = [];

    /// <summary>Bodies of the signed POSTs, keyed by the order they arrived in.</summary>
    public List<string> PostBodies { get; } = [];

    /// <summary>The order status <c>newOrder</c> answers with. <c>ready</c> is the cached-authorization case.</summary>
    public string OrderStatus { get; set; } = "pending";

    /// <summary>The authorization status the authorization document answers with.</summary>
    public string AuthorizationStatus { get; set; } = "pending";

    /// <summary>How many more times the directory GET fails at the transport before it answers.</summary>
    public int DirectoryTransportFailures { get; set; }

    /// <summary>How many more times a signed POST answers 500 before it succeeds.</summary>
    public int SignedPostServerFailures { get; set; }

    /// <summary>How many more times a signed POST answers <c>badNonce</c> before it succeeds.</summary>
    public int BadNonceRefusals { get; set; }

    /// <summary>Whether the whole order should be refused with a rate-limit problem document.</summary>
    public bool RateLimited { get; set; }

    /// <summary>Creates the authority with a parseable leaf certificate to hand back.</summary>
    /// <param name="certificatePem">The PEM chain the download endpoint returns.</param>
    public FakeAcmeAuthority(string certificatePem)
    {
        _certificatePem = certificatePem;
    }

    /// <summary>Answers one request.</summary>
    /// <param name="request">The request the client sent.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>The canned response for that URL.</returns>
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var url = request.RequestUri!.ToString();
        Requests.Add($"{request.Method} {url}");

        if (request.Content is not null)
        {
            PostBodies.Add(await request.Content.ReadAsStringAsync(cancellationToken));
        }

        if (url == DirectoryUrl)
        {
            if (DirectoryTransportFailures > 0)
            {
                DirectoryTransportFailures--;
                throw new HttpRequestException("the authority is unreachable");
            }

            return Json(HttpStatusCode.OK, new
            {
                newNonce = BaseUrl + "/nonce",
                newAccount = BaseUrl + "/new-acct",
                newOrder = BaseUrl + "/new-order",
            });
        }

        if (url == BaseUrl + "/nonce")
        {
            return WithNonce(new HttpResponseMessage(HttpStatusCode.OK));
        }

        if (RateLimited)
        {
            return Problem(HttpStatusCode.TooManyRequests, "urn:ietf:params:acme:error:rateLimited");
        }

        if (BadNonceRefusals > 0)
        {
            BadNonceRefusals--;
            return Problem(HttpStatusCode.BadRequest, "urn:ietf:params:acme:error:badNonce");
        }

        if (SignedPostServerFailures > 0)
        {
            SignedPostServerFailures--;
            return WithNonce(new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent(string.Empty),
            });
        }

        return url switch
        {
            BaseUrl + "/new-acct" => Located(Json(HttpStatusCode.Created, new { status = "valid" }), "/acct/1"),
            BaseUrl + "/new-order" => Located(
                Json(HttpStatusCode.Created, new
                {
                    status = OrderStatus,
                    authorizations = new[] { BaseUrl + "/authz/1" },
                    finalize = BaseUrl + "/finalize",
                }),
                "/order/1"),
            BaseUrl + "/authz/1" => Json(HttpStatusCode.OK, new
            {
                status = _challengeAccepted ? "valid" : AuthorizationStatus,
                challenges = new[] { new { type = "http-01", url = BaseUrl + "/chall/1", token = "tok3n" } },
            }),
            BaseUrl + "/chall/1" => AcceptChallenge(),
            BaseUrl + "/finalize" => Json(HttpStatusCode.OK, new { status = "valid" }),
            BaseUrl + "/order/1" => Json(HttpStatusCode.OK, new
            {
                status = "valid",
                certificate = BaseUrl + "/cert/1",
            }),
            BaseUrl + "/cert/1" => WithNonce(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_certificatePem),
            }),
            _ => Problem(HttpStatusCode.NotFound, "urn:ietf:params:acme:error:malformed"),
        };
    }

    /// <summary>Accepts the challenge, after which the authorization polls as valid.</summary>
    /// <returns>The challenge document.</returns>
    /// <remarks>
    /// A real authority validates asynchronously: the challenge POST is accepted, and the client then
    /// polls the AUTHORIZATION until it turns valid. Answering "valid" immediately would let a client
    /// that never polled pass.
    /// </remarks>
    private HttpResponseMessage AcceptChallenge()
    {
        _challengeAccepted = true;
        return Json(HttpStatusCode.OK, new { status = "processing" });
    }

    /// <summary>How many times a URL was requested.</summary>
    /// <param name="url">The absolute URL to count.</param>
    /// <returns>The number of requests to it.</returns>
    public int CountOf(string url)
    {
        return Requests.Count(entry =>
        {
            return entry.EndsWith(" " + url, StringComparison.Ordinal);
        });
    }

    /// <summary>Builds a JSON response carrying a fresh nonce.</summary>
    /// <param name="status">The status to answer with.</param>
    /// <param name="body">The object to serialize.</param>
    /// <returns>The response.</returns>
    private static HttpResponseMessage Json(HttpStatusCode status, object body)
    {
        return WithNonce(new HttpResponseMessage(status)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
        });
    }

    /// <summary>Builds an RFC 7807 problem document response.</summary>
    /// <param name="status">The status to answer with.</param>
    /// <param name="type">The ACME problem type URN.</param>
    /// <returns>The response.</returns>
    private static HttpResponseMessage Problem(HttpStatusCode status, string type)
    {
        return WithNonce(new HttpResponseMessage(status)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { type, detail = "SECRET-DETAIL-PROSE" }),
                Encoding.UTF8,
                "application/problem+json"),
        });
    }

    /// <summary>Adds the <c>Location</c> header ACME puts identity in.</summary>
    /// <param name="response">The response to decorate.</param>
    /// <param name="path">The location path, relative to the base URL.</param>
    /// <returns>The same response.</returns>
    private static HttpResponseMessage Located(HttpResponseMessage response, string path)
    {
        response.Headers.Location = new Uri(BaseUrl + path);
        return response;
    }

    /// <summary>Adds the anti-replay nonce every ACME response hands out.</summary>
    /// <param name="response">The response to decorate.</param>
    /// <returns>The same response.</returns>
    private static HttpResponseMessage WithNonce(HttpResponseMessage response)
    {
        response.Headers.Add("Replay-Nonce", Guid.NewGuid().ToString("N"));
        return response;
    }
}
