using System.Net;
using System.Security.Cryptography;
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

    /// <summary>The challenges accepted so far, by number, after which their authorizations are valid.</summary>
    private readonly HashSet<int> _acceptedChallenges = [];

    /// <summary>Every request this authority received, in order, as "METHOD url".</summary>
    public List<string> Requests { get; } = [];

    /// <summary>Bodies of the signed POSTs, keyed by the order they arrived in.</summary>
    public List<string> PostBodies { get; } = [];

    /// <summary>Every signed POST as the URL it went to and the payload inside its JWS, in order.</summary>
    public List<(string Url, string Payload)> Posts { get; } = [];

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

    /// <summary>Whether the authority judges the answered challenge invalid instead of valid.</summary>
    public bool FailsValidation { get; set; }

    /// <summary>The challenge type every authorization offers: <c>http-01</c>, or <c>dns-01</c> for a wildcard order.</summary>
    public string ChallengeType { get; set; } = "http-01";

    /// <summary>How many authorizations an order carries: one per identifier, so two for a wildcard order.</summary>
    public int AuthorizationCount { get; set; } = 1;

    /// <summary>
    /// The problem type an invalid challenge reports in its <c>error</c> member, or null for none. Only read
    /// when <see cref="FailsValidation"/> is set.
    /// </summary>
    public string? InvalidProblemType { get; set; }

    /// <summary>The prose the invalid challenge's problem carries, which no returned error or log line may contain.</summary>
    public string InvalidProblemDetail { get; set; } = "SECRET-DETAIL-PROSE";

    /// <summary>Whether <c>finalize</c> is refused as not ready while any authorization of a multi-authorization order is unanswered.</summary>
    public bool FinalizeRequiresEveryAuthorization { get; set; } = true;

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
            var body = await request.Content.ReadAsStringAsync(cancellationToken);
            PostBodies.Add(body);
            Posts.Add((url, PayloadOf(body)));
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

        if (TryAuthorizationNumber(url, "/authz/", out var authorization))
        {
            return AuthorizationDocument(authorization);
        }

        if (TryAuthorizationNumber(url, "/chall/", out var challenge))
        {
            return AcceptChallenge(challenge);
        }

        return url switch
        {
            BaseUrl + "/new-acct" => Located(Json(HttpStatusCode.Created, new { status = "valid" }), "/acct/1"),
            BaseUrl + "/new-order" => Located(
                Json(HttpStatusCode.Created, new
                {
                    status = OrderStatus,
                    authorizations = Enumerable.Range(1, AuthorizationCount).Select(number =>
                    {
                        return BaseUrl + "/authz/" + number;
                    }).ToArray(),
                    finalize = BaseUrl + "/finalize",
                }),
                "/order/1"),
            BaseUrl + "/finalize" => Finalize(),
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

    /// <summary>The token authorization number <paramref name="number"/> offers; the first keeps the original token.</summary>
    /// <param name="number">The authorization's number, from one.</param>
    /// <returns>The token.</returns>
    public static string TokenOf(int number)
    {
        return number == 1 ? "tok3n" : "tok3n" + number;
    }

    /// <summary>The identifier values the order was placed with, read out of the signed <c>newOrder</c> payload.</summary>
    /// <returns>The values in request order; empty when no order was placed.</returns>
    public IReadOnlyList<string> OrderedIdentifiers()
    {
        var payload = Posts.FirstOrDefault(post =>
        {
            return post.Url == BaseUrl + "/new-order";
        }).Payload;
        if (string.IsNullOrEmpty(payload))
        {
            return [];
        }

        return JsonDocument.Parse(payload).RootElement.GetProperty("identifiers").EnumerateArray()
            .Select(identifier =>
            {
                return identifier.GetProperty("value").GetString() ?? string.Empty;
            })
            .ToList();
    }

    /// <summary>The DER certificate signing request the client finalized with, read out of the signed payload.</summary>
    /// <returns>The DER bytes; empty when the order was never finalized.</returns>
    public byte[] FinalizedCsr()
    {
        var payload = Posts.LastOrDefault(post =>
        {
            return post.Url == BaseUrl + "/finalize";
        }).Payload;
        if (string.IsNullOrEmpty(payload))
        {
            return [];
        }

        return Base64UrlDecode(JsonDocument.Parse(payload).RootElement.GetProperty("csr").GetString() ?? string.Empty);
    }

    /// <summary>Reads the RFC 7638 thumbprint of the account key out of the <c>newAccount</c> request's JWK.</summary>
    /// <returns>The thumbprint, computed here from the public key and not asked of the client under test.</returns>
    public string AccountThumbprint()
    {
        var body = PostBodies[Posts.FindIndex(post =>
        {
            return post.Url == BaseUrl + "/new-acct";
        })];
        var protectedHeader = JsonDocument.Parse(Encoding.UTF8.GetString(
            Base64UrlDecode(JsonDocument.Parse(body).RootElement.GetProperty("protected").GetString() ?? string.Empty)));
        var jwk = protectedHeader.RootElement.GetProperty("jwk");
        var canonical = $"{{\"crv\":\"{jwk.GetProperty("crv").GetString()}\",\"kty\":\"{jwk.GetProperty("kty").GetString()}\","
            + $"\"x\":\"{jwk.GetProperty("x").GetString()}\",\"y\":\"{jwk.GetProperty("y").GetString()}\"}}";

        return Base64UrlEncode(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    /// <summary>The TXT value a correct client publishes for an authorization, computed here independently.</summary>
    /// <param name="number">The authorization's number, from one.</param>
    /// <returns>The base64url SHA-256 of the key authorization, which is forty-three characters.</returns>
    public string ExpectedTxtValue(int number)
    {
        return Base64UrlEncode(SHA256.HashData(Encoding.UTF8.GetBytes($"{TokenOf(number)}.{AccountThumbprint()}")));
    }

    /// <summary>The JWS payload of a signed request body.</summary>
    /// <param name="body">The request body: a flattened JWS.</param>
    /// <returns>The decoded payload text; empty for a POST-as-GET.</returns>
    private static string PayloadOf(string body)
    {
        try
        {
            var payload = JsonDocument.Parse(body).RootElement.GetProperty("payload").GetString() ?? string.Empty;

            return Encoding.UTF8.GetString(Base64UrlDecode(payload));
        }
        catch (JsonException)
        {
            return string.Empty;
        }
    }

    /// <summary>Decodes unpadded base64url.</summary>
    /// <param name="text">The encoded text.</param>
    /// <returns>The bytes.</returns>
    private static byte[] Base64UrlDecode(string text)
    {
        var padded = text.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight(padded.Length + ((4 - (padded.Length % 4)) % 4), '=');

        return Convert.FromBase64String(padded);
    }

    /// <summary>Encodes unpadded base64url.</summary>
    /// <param name="bytes">The bytes.</param>
    /// <returns>The encoded text.</returns>
    private static string Base64UrlEncode(byte[] bytes)
    {
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    /// <summary>Reads the number out of <c>…/authz/N</c> or <c>…/chall/N</c>.</summary>
    /// <param name="url">The request URL.</param>
    /// <param name="segment">The path segment before the number, with slashes.</param>
    /// <param name="number">The number, when the URL has the shape.</param>
    /// <returns>Whether the URL has the shape.</returns>
    private static bool TryAuthorizationNumber(string url, string segment, out int number)
    {
        number = 0;

        return url.StartsWith(BaseUrl + segment, StringComparison.Ordinal)
            && int.TryParse(url[(BaseUrl.Length + segment.Length)..], out number);
    }

    /// <summary>The authorization document: pending until its challenge is accepted, then valid or invalid.</summary>
    /// <param name="number">The authorization's number.</param>
    /// <returns>The response.</returns>
    private HttpResponseMessage AuthorizationDocument(int number)
    {
        var accepted = _acceptedChallenges.Contains(number);
        var status = accepted ? (FailsValidation ? "invalid" : "valid") : AuthorizationStatus;
        object challengeBody = accepted && FailsValidation && InvalidProblemType is not null
            ? new
            {
                type = ChallengeType,
                url = BaseUrl + "/chall/" + number,
                token = TokenOf(number),
                error = new { type = InvalidProblemType, detail = InvalidProblemDetail },
            }
            : new { type = ChallengeType, url = BaseUrl + "/chall/" + number, token = TokenOf(number) };

        return Json(HttpStatusCode.OK, new { status, challenges = new[] { challengeBody } });
    }

    /// <summary>Answers <c>finalize</c>: refused as not ready when an authorization of a multi-authorization order is unanswered.</summary>
    /// <returns>The response.</returns>
    private HttpResponseMessage Finalize()
    {
        var unanswered = AuthorizationCount > 1
            && OrderStatus != "ready"
            && FinalizeRequiresEveryAuthorization
            && AuthorizationStatus != "valid"
            && _acceptedChallenges.Count < AuthorizationCount;

        return unanswered
            ? Problem(HttpStatusCode.Forbidden, "urn:ietf:params:acme:error:orderNotReady")
            : Json(HttpStatusCode.OK, new { status = "valid" });
    }

    /// <summary>Accepts a challenge, after which its authorization polls as valid.</summary>
    /// <param name="number">The challenge's number.</param>
    /// <returns>The challenge document.</returns>
    /// <remarks>
    /// A real authority validates asynchronously: the challenge POST is accepted, and the client then
    /// polls the AUTHORIZATION until it turns valid. Answering "valid" immediately would let a client
    /// that never polled pass.
    /// </remarks>
    private HttpResponseMessage AcceptChallenge(int number)
    {
        _acceptedChallenges.Add(number);
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
