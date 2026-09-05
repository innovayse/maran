using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Maran.Modules.Ssl.Interfaces;
using Maran.Modules.Ssl.Models;
using Maran.Modules.Ssl.Options;
using Maran.Modules.Ssl.Resources;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Maran.Modules.Ssl.Services;

/// <summary>
/// Orders certificates from an ACME certificate authority over HTTP-01, and nothing else (spec §11).
/// </summary>
/// <remarks>
/// HTTP-01 over the site's own document root is the whole of what this supports, deliberately.
/// DNS-01 — the only challenge that can issue a wildcard — requires the panel to write a TXT record,
/// which requires a DNS module that is not in v1 at all. Offering wildcard issuance with no way to
/// answer its challenge would be an endpoint that always fails.
///
/// Every outbound call goes through the named <c>acme</c> <see cref="HttpClient"/>, which the Host
/// has already wrapped in its own resilience pipeline — a certificate authority is not a unix socket
/// and does not share the agent's timeout, and its rate limits mean a rejected order must not be
/// retried (rules/csharp.md "Every outbound call goes through a named resilience pipeline").
///
/// The authority's own text never leaves this type. A problem document may quote what the authority
/// could not parse, and every failure here is therefore returned as a code and logged as a sentence
/// (rules/security.md item 8).
/// </remarks>
public sealed class AcmeClient : IAcmeClient
{
    /// <summary>Content type of every signed ACME request body (RFC 8555 §6.2).</summary>
    private const string JoseContentType = "application/jose+json";

    /// <summary>Response header carrying the anti-replay nonce for the next request.</summary>
    private const string NonceHeader = "Replay-Nonce";

    /// <summary>The empty JWS payload that turns a POST into a read (RFC 8555 §6.3).</summary>
    private const string PostAsGet = "";

    /// <summary>The order status meaning "every identifier is validated; send the CSR" (RFC 8555 §7.1.6).</summary>
    private const string OrderReady = "ready";

    /// <summary>The authorization status meaning "control of this name is already proven".</summary>
    private const string AuthorizationValid = "valid";

    /// <summary>Pre-compiled log delegate for a failed order, so a renewal pass logs cheaply.</summary>
    /// <remarks>
    /// The third placeholder is the panel's own error CODE, and it is named so. An earlier version
    /// called it <c>AuthorityText</c> while passing the same code, which promised an operator a
    /// description the line could never contain. The authority's own machine fields — status and
    /// problem type — are logged by <see cref="AcmeSession"/>, which is the layer that has them.
    /// </remarks>
    private static readonly Action<ILogger, string, string, string, Exception?> LogOrderFailure =
        LoggerMessage.Define<string, string, string>(
            LogLevel.Warning,
            new EventId(1, nameof(AcmeClient)),
            "ACME order for {Domain} failed at {Stage} with {ErrorCode}");

    /// <summary>Builds the named, already-governed client every call below uses.</summary>
    private readonly IHttpClientFactory _httpClientFactory;

    /// <summary>The authority's address and the panel's contact details.</summary>
    private readonly AcmeOptions _options;

    /// <summary>Places and removes the challenge file, through the agent, as the account.</summary>
    private readonly IAcmeChallengeWriter _challengeWriter;

    /// <summary>The panel's ACME registration, created on first use and reused after.</summary>
    private readonly AcmeAccountStore _accountStore;

    /// <summary>The injected time source; never the ambient clock (rules/csharp.md).</summary>
    private readonly IClock _clock;

    /// <summary>Where the authority's own text goes, since a returned <see cref="Error"/> carries only a code.</summary>
    private readonly ILogger<AcmeClient> _logger;

    /// <summary>Creates the client.</summary>
    /// <param name="httpClientFactory">Supplies the named, pipeline-wrapped ACME client.</param>
    /// <param name="options">The authority's address and the panel's contact details.</param>
    /// <param name="challengeWriter">Places and removes the HTTP-01 challenge file.</param>
    /// <param name="accountStore">The panel's ACME registration.</param>
    /// <param name="clock">The injected time source, which bounds every wait below.</param>
    /// <param name="logger">Sink for the authority's diagnostic text.</param>
    public AcmeClient(
        IHttpClientFactory httpClientFactory,
        IOptions<AcmeOptions> options,
        IAcmeChallengeWriter challengeWriter,
        AcmeAccountStore accountStore,
        IClock clock,
        ILogger<AcmeClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _challengeWriter = challengeWriter;
        _accountStore = accountStore;
        _clock = clock;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<Result<IssuedCertificate>> OrderAsync(
        AcmeOrderRequest request,
        CancellationToken cancellationToken)
    {
        using var http = _httpClientFactory.CreateClient(AcmeOptions.HttpClientName);

        var fetched = await ReadJsonAsync(http, _options.DirectoryUrl, _logger, cancellationToken);
        if (fetched is not { } directory)
        {
            return Fail(request.Domain, "directory", Error.Of(nameof(ErrorMessages.AcmeAuthorityUnreachable), ErrorType.Unavailable));
        }

        var registration = await _accountStore.GetOrCreateAsync(
            http, directory, _options.DirectoryUrl, _options.ContactEmail, cancellationToken);
        if (!registration.IsSuccess)
        {
            return Result<IssuedCertificate>.Fail(registration.Error!);
        }

        using var signer = registration.Value.Signer;
        var session = new AcmeSession(http, signer, registration.Value.AccountUrl, _logger);
        await session.RefreshNonceAsync(Url(directory, "newNonce"), cancellationToken);

        return await PlaceOrderAsync(session, directory, request, cancellationToken);
    }

    /// <summary>Reads one string member of a JSON object, or the empty string when it is absent.</summary>
    /// <param name="element">The object to read.</param>
    /// <param name="name">The member name.</param>
    /// <returns>The member's text, or the empty string.</returns>
    private static string Url(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
    }

    /// <summary>Fetches the directory document — the one ACME call that needs no signature.</summary>
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
    private static async Task<JsonElement?> ReadJsonAsync(
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

    /// <summary>Builds the HTTP-01 key authorization: the token joined to the account key's thumbprint.</summary>
    /// <param name="token">The challenge token from the authority.</param>
    /// <param name="thumbprint">The account key's RFC 7638 thumbprint.</param>
    /// <returns>Exactly the bytes the authority will fetch and compare.</returns>
    private static string KeyAuthorization(string token, string thumbprint)
    {
        return string.Create(CultureInfo.InvariantCulture, $"{token}.{thumbprint}");
    }

    /// <summary>Generates a fresh certificate key and the CSR that asks for one domain.</summary>
    /// <param name="domain">The domain to request.</param>
    /// <param name="key">The generated key, which the caller owns and must dispose.</param>
    /// <returns>The DER-encoded certificate signing request.</returns>
    /// <remarks>
    /// A NEW key on every issuance, including every renewal. Reusing a key across renewals means one
    /// compromise never expires; generating one is a few milliseconds, and the old key is discarded
    /// with the old certificate.
    /// </remarks>
    private static byte[] CreateCsr(string domain, out ECDsa key)
    {
        var created = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        try
        {
            var subjectAlternativeNames = new SubjectAlternativeNameBuilder();
            subjectAlternativeNames.AddDnsName(domain);

            var csr = new CertificateRequest($"CN={domain}", created, HashAlgorithmName.SHA256);
            csr.CertificateExtensions.Add(subjectAlternativeNames.Build());
            var request = csr.CreateSigningRequest();

            key = created;
            return request;
        }
        catch
        {
            // The out parameter is assigned only on the success path, so a throw here leaves the
            // caller with nothing to dispose — which is why it has to be disposed here.
            created.Dispose();
            throw;
        }
    }

    /// <summary>Runs the order from creation to issued material.</summary>
    /// <param name="session">The signed conversation with the authority.</param>
    /// <param name="directory">The authority's directory document.</param>
    /// <param name="request">The domain and the account whose document root answers.</param>
    /// <param name="cancellationToken">Cancellation for the whole order.</param>
    /// <returns>The issued material, or a typed failure.</returns>
    private async Task<Result<IssuedCertificate>> PlaceOrderAsync(
        AcmeSession session,
        JsonElement directory,
        AcmeOrderRequest request,
        CancellationToken cancellationToken)
    {
        var identifiers = JsonSerializer.Serialize(new
        {
            identifiers = new[] { new { type = "dns", value = request.Domain } },
        });

        var order = await session.PostAsync(Url(directory, "newOrder"), identifiers, cancellationToken);
        if (!order.IsSuccess)
        {
            return Fail(request.Domain, "newOrder", order.Error!);
        }

        var orderUrl = order.Value.Location;
        var authorizationUrl = FirstAuthorizationUrl(order.Value.Body);
        if (authorizationUrl.Length == 0 || orderUrl.Length == 0)
        {
            return Fail(request.Domain, "newOrder", Error.Of(nameof(ErrorMessages.AcmeOrderRejected), ErrorType.Failure));
        }

        // RFC 8555 §7.1.6: an order for an identifier this account has ALREADY validated comes back
        // "ready" with nothing to prove, because authorities cache authorizations (Let's Encrypt for
        // thirty days). Renewal is exactly that case, so this branch is the module's main path and
        // not an edge: without it, renewal writes a challenge file, POSTs to a challenge on a valid
        // authorization, is answered "malformed", and throws away an order that was ready to finalize.
        if (!string.Equals(Url(order.Value.Body, "status"), OrderReady, StringComparison.Ordinal))
        {
            var validated = await ValidateAsync(session, authorizationUrl, request, cancellationToken);
            if (!validated.IsSuccess)
            {
                return Result<IssuedCertificate>.Fail(validated.Error!);
            }
        }

        return await FinalizeAsync(session, order.Value.Body, orderUrl, request.Domain, cancellationToken);
    }

    /// <summary>Reads the first authorization URL out of a newly created order.</summary>
    /// <param name="order">The order object the authority returned.</param>
    /// <returns>The URL, or the empty string when the order carries none.</returns>
    /// <remarks>
    /// The first and only: this client orders for exactly one identifier, so an order with a second
    /// authorization would mean the authority answered a request nobody made.
    /// </remarks>
    private static string FirstAuthorizationUrl(JsonElement order)
    {
        if (!order.TryGetProperty("authorizations", out var authorizations)
            || authorizations.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        foreach (var authorization in authorizations.EnumerateArray())
        {
            return authorization.GetString() ?? string.Empty;
        }

        return string.Empty;
    }

    /// <summary>Answers the HTTP-01 challenge and waits, bounded, for the authority to accept it.</summary>
    /// <param name="session">The signed conversation with the authority.</param>
    /// <param name="authorizationUrl">The authorization to satisfy.</param>
    /// <param name="request">The domain and the account whose document root answers.</param>
    /// <param name="cancellationToken">Cancellation for the wait.</param>
    /// <returns>Success once the authorization is valid, or a typed failure.</returns>
    /// <remarks>
    /// The challenge file is removed in a <c>finally</c>, and its removal can never turn a successful
    /// validation into a failure: a consumed token proves nothing to anybody, while discarding a
    /// valid authorization over a failed unlink would cost a real certificate.
    /// </remarks>
    private async Task<Result<bool>> ValidateAsync(
        AcmeSession session,
        string authorizationUrl,
        AcmeOrderRequest request,
        CancellationToken cancellationToken)
    {
        var authorization = await session.PostAsync(authorizationUrl, PostAsGet, cancellationToken);
        if (!authorization.IsSuccess)
        {
            return Result<bool>.Fail(Error.Of(nameof(ErrorMessages.AcmeOrderRejected), ErrorType.Failure));
        }

        // The same cached-authorization case as the order status above, checked separately because
        // the two are not redundant: an order can be "pending" while one of its authorizations is
        // already "valid" (a multi-identifier order, or an authority that has not yet recomputed the
        // order). Answering a challenge on a valid authorization is refused as "malformed".
        if (string.Equals(Url(authorization.Value.Body, "status"), AuthorizationValid, StringComparison.Ordinal))
        {
            return Result<bool>.Ok(true);
        }

        if (!TryReadHttpChallenge(authorization.Value.Body, out var challengeUrl, out var token))
        {
            return Result<bool>.Fail(Error.Of(nameof(ErrorMessages.AcmeChallengeUnavailable), ErrorType.Unavailable));
        }

        var written = await _challengeWriter.WriteAsync(
            request.AccountUsername,
            request.Domain,
            token,
            KeyAuthorization(token, session.Thumbprint),
            cancellationToken);
        if (!written.IsSuccess)
        {
            return Result<bool>.Fail(Error.Of(nameof(ErrorMessages.AcmeChallengeWriteFailed), ErrorType.Failure));
        }

        try
        {
            var triggered = await session.PostAsync(challengeUrl, "{}", cancellationToken);
            if (!triggered.IsSuccess)
            {
                return Result<bool>.Fail(Error.Of(nameof(ErrorMessages.AcmeValidationFailed), ErrorType.Failure));
            }

            return await PollAsync(session, authorizationUrl, cancellationToken);
        }
        finally
        {
            // Deliberately unchecked: see the remarks above. The agent logs its own failure.
            await _challengeWriter.RemoveAsync(
                request.AccountUsername, request.Domain, token, CancellationToken.None);
        }
    }

    /// <summary>Finds the HTTP-01 challenge among the ones the authority offered.</summary>
    /// <param name="authorization">The authorization object.</param>
    /// <param name="challengeUrl">The challenge's URL, when one was found.</param>
    /// <param name="token">The challenge's token, when one was found.</param>
    /// <returns><c>true</c> when an HTTP-01 challenge is present with both fields.</returns>
    private static bool TryReadHttpChallenge(JsonElement authorization, out string challengeUrl, out string token)
    {
        challengeUrl = string.Empty;
        token = string.Empty;

        if (!authorization.TryGetProperty("challenges", out var challenges)
            || challenges.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var challenge in challenges.EnumerateArray())
        {
            if (!string.Equals(Url(challenge, "type"), "http-01", StringComparison.Ordinal))
            {
                continue;
            }

            challengeUrl = Url(challenge, "url");
            token = Url(challenge, "token");
            return challengeUrl.Length > 0 && token.Length > 0;
        }

        return false;
    }

    /// <summary>Polls one authorization until it is valid, invalid, or the bound is reached.</summary>
    /// <param name="session">The signed conversation with the authority.</param>
    /// <param name="authorizationUrl">The authorization to poll.</param>
    /// <param name="cancellationToken">Cancellation for the wait.</param>
    /// <returns>Success when valid; <c>AcmeValidationFailed</c> or <c>AcmeValidationTimedOut</c> otherwise.</returns>
    /// <remarks>
    /// The deadline is computed from the injected clock before the first poll, so the wait is bounded
    /// by construction rather than by hoping the authority eventually answers something terminal. An
    /// unbounded poll here is not a slow renewal, it is a renewal job that never returns and a
    /// certificate that silently stops being renewed.
    /// </remarks>
    private async Task<Result<bool>> PollAsync(
        AcmeSession session,
        string authorizationUrl,
        CancellationToken cancellationToken)
    {
        var deadline = _clock.UtcNow + _options.ValidationTimeout;

        while (_clock.UtcNow < deadline)
        {
            await Task.Delay(_options.PollInterval, cancellationToken);

            var polled = await session.PostAsync(authorizationUrl, PostAsGet, cancellationToken);
            if (!polled.IsSuccess)
            {
                return Result<bool>.Fail(Error.Of(nameof(ErrorMessages.AcmeValidationFailed), ErrorType.Failure));
            }

            var status = Url(polled.Value.Body, "status");
            if (string.Equals(status, "valid", StringComparison.Ordinal))
            {
                return Result<bool>.Ok(true);
            }

            if (string.Equals(status, "invalid", StringComparison.Ordinal)
                || string.Equals(status, "revoked", StringComparison.Ordinal)
                || string.Equals(status, "deactivated", StringComparison.Ordinal))
            {
                return Result<bool>.Fail(Error.Of(nameof(ErrorMessages.AcmeValidationFailed), ErrorType.Failure));
            }
        }

        return Result<bool>.Fail(Error.Of(nameof(ErrorMessages.AcmeValidationTimedOut), ErrorType.Unavailable));
    }

    /// <summary>Finalizes a validated order and downloads the issued chain.</summary>
    /// <param name="session">The signed conversation with the authority.</param>
    /// <param name="order">The order object, which names the finalize endpoint.</param>
    /// <param name="orderUrl">The order's own URL, polled until the certificate is ready.</param>
    /// <param name="domain">The domain being issued for, for the CSR and the log line.</param>
    /// <param name="cancellationToken">Cancellation for the wait.</param>
    /// <returns>The issued material, or a typed failure.</returns>
    private async Task<Result<IssuedCertificate>> FinalizeAsync(
        AcmeSession session,
        JsonElement order,
        string orderUrl,
        string domain,
        CancellationToken cancellationToken)
    {
        var csr = CreateCsr(domain, out var key);
        using (key)
        {
            var body = JsonSerializer.Serialize(new { csr = AcmeSigner.Base64Url(csr) });
            var finalized = await session.PostAsync(Url(order, "finalize"), body, cancellationToken);
            if (!finalized.IsSuccess)
            {
                return Fail(domain, "finalize", Error.Of(nameof(ErrorMessages.AcmeOrderRejected), ErrorType.Failure));
            }

            var certificateUrl = await PollOrderAsync(session, orderUrl, cancellationToken);
            if (!certificateUrl.IsSuccess)
            {
                return Result<IssuedCertificate>.Fail(certificateUrl.Error!);
            }

            var chain = await session.PostForTextAsync(certificateUrl.Value, PostAsGet, cancellationToken);
            if (!chain.IsSuccess)
            {
                return Fail(domain, "download", Error.Of(nameof(ErrorMessages.AcmeOrderRejected), ErrorType.Failure));
            }

            return Materialize(chain.Value, key, domain);
        }
    }

    /// <summary>Polls the order until it carries a certificate URL, or the bound is reached.</summary>
    /// <param name="session">The signed conversation with the authority.</param>
    /// <param name="orderUrl">The order to poll.</param>
    /// <param name="cancellationToken">Cancellation for the wait.</param>
    /// <returns>The certificate URL, or a typed failure.</returns>
    private async Task<Result<string>> PollOrderAsync(
        AcmeSession session,
        string orderUrl,
        CancellationToken cancellationToken)
    {
        var deadline = _clock.UtcNow + _options.ValidationTimeout;

        while (_clock.UtcNow < deadline)
        {
            var polled = await session.PostAsync(orderUrl, PostAsGet, cancellationToken);
            if (!polled.IsSuccess)
            {
                return Result<string>.Fail(Error.Of(nameof(ErrorMessages.AcmeOrderRejected), ErrorType.Failure));
            }

            var certificateUrl = Url(polled.Value.Body, "certificate");
            if (certificateUrl.Length > 0)
            {
                return Result<string>.Ok(certificateUrl);
            }

            if (string.Equals(Url(polled.Value.Body, "status"), "invalid", StringComparison.Ordinal))
            {
                return Result<string>.Fail(Error.Of(nameof(ErrorMessages.AcmeOrderRejected), ErrorType.Failure));
            }

            await Task.Delay(_options.PollInterval, cancellationToken);
        }

        return Result<string>.Fail(Error.Of(nameof(ErrorMessages.AcmeValidationTimedOut), ErrorType.Unavailable));
    }

    /// <summary>Turns a downloaded chain and its key into the material an install takes.</summary>
    /// <param name="chainPem">The PEM chain the authority returned, leaf first.</param>
    /// <param name="key">The key the CSR was made with.</param>
    /// <param name="domain">The domain, for the log line if the chain will not parse.</param>
    /// <returns>The material, or <c>AcmeCertificateUnreadable</c>.</returns>
    private Result<IssuedCertificate> Materialize(string chainPem, ECDsa key, string domain)
    {
        try
        {
            using var leaf = X509Certificate2.CreateFromPem(chainPem);
            return Result<IssuedCertificate>.Ok(new IssuedCertificate(
                chainPem,
                key.ExportPkcs8PrivateKeyPem(),
                new DateTimeOffset(leaf.NotAfter.ToUniversalTime(), TimeSpan.Zero)));
        }
        catch (CryptographicException exception)
        {
            // The chain is logged by NEITHER branch: a certificate is public, but this text is
            // whatever the authority actually sent, and a client that logs an unparseable response
            // is a client that logs whatever an unexpected response contains.
            LogOrderFailure(_logger, domain, "parse", exception.GetType().Name, null);
            return Result<IssuedCertificate>.Fail(Error.Of(nameof(ErrorMessages.AcmeCertificateUnreadable), ErrorType.Failure));
        }
    }

    /// <summary>Logs one failed stage and returns it as the typed failure.</summary>
    /// <param name="domain">The domain the order was for.</param>
    /// <param name="stage">Which step refused, so an operator can tell an unreachable authority from a rejected order.</param>
    /// <param name="error">The typed failure to answer with, code and kind together.</param>
    /// <returns>The failed result carrying <paramref name="error"/>.</returns>
    private Result<IssuedCertificate> Fail(string domain, string stage, Error error)
    {
        LogOrderFailure(_logger, domain, stage, error.Code, null);
        return Result<IssuedCertificate>.Fail(error);
    }

    /// <summary>The media type header every signed request carries.</summary>
    /// <returns>The parsed <c>application/jose+json</c> header value.</returns>
    internal static MediaTypeHeaderValue JoseMediaType()
    {
        return new MediaTypeHeaderValue(JoseContentType);
    }

    /// <summary>The name of the response header carrying the next nonce.</summary>
    /// <returns>The header name, shared with <see cref="AcmeSession"/>.</returns>
    internal static string NonceHeaderName()
    {
        return NonceHeader;
    }

    /// <summary>UTF-8 without a byte-order mark, which no ACME body may carry.</summary>
    /// <returns>The encoding every request body is written with.</returns>
    internal static Encoding BodyEncoding()
    {
        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    }
}
