using System.Net;
using System.Text.Json;
using Maran.Modules.Ssl.Models;
using Maran.Modules.Ssl.Resources;
using Microsoft.Extensions.Logging;

namespace Maran.Modules.Ssl.Services;

/// <summary>
/// One signed conversation with an ACME authority: it holds the anti-replay nonce and turns a URL
/// plus a payload into a signed POST (RFC 8555 §6).
/// </summary>
/// <remarks>
/// Nonce handling is the reason this is a type and not a method. ACME requires every signed request
/// to carry a nonce the authority issued and has not seen before, and every response — success or
/// failure — hands out the next one. Threading that through call sites is how a client ends up
/// re-using a nonce and failing with <c>badNonce</c> on a request that was otherwise fine, so the
/// state lives here and the single retry that <c>badNonce</c> is supposed to get lives here too.
///
/// A session is not thread-safe and is not meant to be: one order is one sequence of requests, and
/// two orders sharing a nonce would take turns invalidating each other's.
///
/// It is also the only place an authority's refusal is described in words. The problem document's
/// machine fields — status and type URN — are logged here; its prose <c>detail</c> is not, and the
/// <see cref="Error"/> that travels outward carries a code and nothing else (rules/security.md item 8).
/// </remarks>
public sealed class AcmeSession
{
    /// <summary>The authority's error type for a nonce it will not accept, which is retried once.</summary>
    private const string BadNonceProblem = "urn:ietf:params:acme:error:badNonce";

    /// <summary>Pre-compiled log delegate for an authority that refused a signed request.</summary>
    private static readonly Action<ILogger, string, int, string, Exception?> LogRefusal =
        LoggerMessage.Define<string, int, string>(
            LogLevel.Warning,
            new EventId(1, nameof(AcmeSession)),
            "The certificate authority refused {Url} with status {Status} and problem type {ProblemType}");

    /// <summary>The named, already-governed ACME client.</summary>
    private readonly HttpClient _http;

    /// <summary>The account key this session signs with.</summary>
    private readonly AcmeSigner _signer;

    /// <summary>The account URL sent as <c>kid</c>, or null while the account is being created.</summary>
    private readonly string? _accountUrl;

    /// <summary>Where an authority's machine-readable refusal is described; never its prose.</summary>
    private readonly ILogger _logger;

    /// <summary>The nonce the next request will carry. Replaced by every response.</summary>
    private string _nonce = string.Empty;

    /// <summary>The <c>Location</c> header of the most recent response, or the empty string.</summary>
    private string _lastLocation = string.Empty;

    /// <summary>Creates a session over an existing account.</summary>
    /// <param name="http">The named, already-governed ACME client.</param>
    /// <param name="signer">The account key to sign with.</param>
    /// <param name="accountUrl">The account URL, or null for the request that creates the account.</param>
    /// <param name="logger">Sink for the authority's machine-readable refusals.</param>
    public AcmeSession(HttpClient http, AcmeSigner signer, string? accountUrl, ILogger logger)
    {
        _http = http;
        _signer = signer;
        _accountUrl = accountUrl;
        _logger = logger;
    }

    /// <summary>The account key's RFC 7638 thumbprint, the second half of every key authorization.</summary>
    public string Thumbprint
    {
        get
        {
            return _signer.JwkThumbprint();
        }
    }

    /// <summary>Fetches a fresh nonce, which every conversation must do before its first signed request.</summary>
    /// <param name="newNonceUrl">The authority's <c>newNonce</c> endpoint.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    public async Task RefreshNonceAsync(string newNonceUrl, CancellationToken cancellationToken)
    {
        // Retried, unlike every signed request below: a HEAD for a nonce is unsigned and has no side
        // effect at the authority beyond minting a value nobody has to use (see AcmeTransport).
        using var response = await AcmeTransport.SendIdempotentAsync(
            _http,
            newNonceUrl,
            () =>
            {
                return new HttpRequestMessage(HttpMethod.Head, new Uri(newNonceUrl));
            },
            _logger,
            cancellationToken);
        if (response is not null)
        {
            CaptureNonce(response);
        }
    }

    /// <summary>Sends one signed request and parses its JSON body.</summary>
    /// <param name="url">The target URL, which is also signed into the protected header.</param>
    /// <param name="payload">The request body, or the empty string for a POST-as-GET.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>The parsed response, or a typed failure carrying only a code.</returns>
    public async Task<Result<AcmeResponse>> PostAsync(
        string url,
        string payload,
        CancellationToken cancellationToken)
    {
        var text = await PostForTextAsync(url, payload, cancellationToken);
        if (!text.IsSuccess)
        {
            return Result<AcmeResponse>.Fail(text.Error!);
        }

        try
        {
            var body = text.Value.Length == 0
                ? default
                : JsonSerializer.Deserialize<JsonElement>(text.Value);
            return Result<AcmeResponse>.Ok(new AcmeResponse(body, _lastLocation));
        }
        catch (JsonException)
        {
            // The authority's text is deliberately not carried into the error: a problem document can
            // quote what it could not parse (rules/security.md item 8).
            return Result<AcmeResponse>.Fail(Error.Of(nameof(ErrorMessages.AcmeAuthorityUnreachable), ErrorType.Unavailable));
        }
    }

    /// <summary>Sends one signed request and returns its body as text, for the PEM chain download.</summary>
    /// <param name="url">The target URL, which is also signed into the protected header.</param>
    /// <param name="payload">The request body, or the empty string for a POST-as-GET.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>The response body, or a typed failure carrying only a code.</returns>
    public async Task<Result<string>> PostForTextAsync(
        string url,
        string payload,
        CancellationToken cancellationToken)
    {
        var first = await SendOnceAsync(url, payload, cancellationToken);
        if (first.Succeeded || !first.RetryableNonce)
        {
            return first.ToResult();
        }

        // One retry, and only for badNonce. The authority has just handed out a usable nonce with
        // that very refusal, so the second attempt is a different request rather than the same one
        // repeated — which is why this is not the resilience pipeline's job (a pipeline would replay
        // an identical, still-invalid body) and why it does not loop.
        var second = await SendOnceAsync(url, payload, cancellationToken);
        return second.ToResult();
    }

    /// <summary>Signs and sends exactly one request, capturing the nonce and location it returns.</summary>
    /// <param name="url">The target URL.</param>
    /// <param name="payload">The request body, or the empty string for a POST-as-GET.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>The outcome, including whether a retry on a fresh nonce is warranted.</returns>
    private async Task<AcmeAttempt> SendOnceAsync(string url, string payload, CancellationToken cancellationToken)
    {
        var jws = _signer.Sign(url, _nonce, payload, _accountUrl);

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(url));
        request.Content = new StringContent(jws, AcmeClient.BodyEncoding());
        request.Content.Headers.ContentType = AcmeClient.JoseMediaType();

        using var response = await _http.SendAsync(request, cancellationToken);
        CaptureNonce(response);
        _lastLocation = response.Headers.Location?.ToString() ?? string.Empty;

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return new AcmeAttempt(true, false, body);
        }

        // Logged HERE, at the only point that has the document, and logged as its machine fields
        // only. Every layer above this one carries a code, so without this line an operator watching
        // an unattended renewal fail nightly has no way to learn whether the authority is rate
        // limiting them, cannot reach the domain, or refused the account (rules/security.md item 8
        // is satisfied by never touching the prose "detail" member).
        var problem = ProblemOf(response.StatusCode, body);
        LogRefusal(_logger, url, problem.Status, problem.Type, null);

        return new AcmeAttempt(false, IsBadNonce(response.StatusCode, body), body);
    }

    /// <summary>Reads the machine-readable half of a problem document.</summary>
    /// <param name="status">The response status.</param>
    /// <param name="body">The response body, which may or may not be a problem document.</param>
    /// <returns>The status and the problem type URN; the type is empty when the body carried none.</returns>
    /// <remarks>
    /// A body that will not parse is not an error here. An authority is entitled to answer a 502 from
    /// a load balancer with HTML, and losing the status because the prose was not JSON would throw
    /// away the more useful of the two fields.
    /// </remarks>
    private static AcmeProblem ProblemOf(HttpStatusCode status, string body)
    {
        try
        {
            var document = JsonSerializer.Deserialize<JsonElement>(body);
            if (document.ValueKind == JsonValueKind.Object
                && document.TryGetProperty("type", out var type)
                && type.ValueKind == JsonValueKind.String)
            {
                return new AcmeProblem((int)status, type.GetString() ?? string.Empty);
            }
        }
        catch (JsonException)
        {
            // Falls through to the status-only answer below.
        }

        return new AcmeProblem((int)status, string.Empty);
    }

    /// <summary>Whether a refusal is the one kind worth retrying with a fresh nonce.</summary>
    /// <param name="status">The response status.</param>
    /// <param name="body">The problem document, read as text.</param>
    /// <returns><c>true</c> for an ACME <c>badNonce</c> problem.</returns>
    private static bool IsBadNonce(HttpStatusCode status, string body)
    {
        return status == HttpStatusCode.BadRequest
            && body.Contains(BadNonceProblem, StringComparison.Ordinal);
    }

    /// <summary>Stores the nonce a response handed out, if it carried one.</summary>
    /// <param name="response">The response to read the header from.</param>
    private void CaptureNonce(HttpResponseMessage response)
    {
        if (response.Headers.TryGetValues(AcmeClient.NonceHeaderName(), out var values))
        {
            foreach (var value in values)
            {
                _nonce = value;
                return;
            }
        }
    }
}
