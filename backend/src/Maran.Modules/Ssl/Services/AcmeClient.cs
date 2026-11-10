using System.Net.Http.Headers;
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
/// Orders certificates from an ACME certificate authority: a plain certificate over HTTP-01, a wildcard
/// certificate over DNS-01 (spec §11).
/// </summary>
/// <remarks>
/// A plain order proves its one name with a file in the site's document root. A wildcard order — which
/// only DNS-01 can prove — asks for the name and <c>*.name</c> together and proves both with a TXT record
/// in a zone this server serves, published through the DNS module's challenge door. The order is the same
/// either way; the part that differs is the <see cref="IAcmeChallengeResponder"/> its request selects.
///
/// The steps live in their own types: the responders answer the challenges, the poller waits for the
/// authority, the finalizer sends the signing request and downloads the chain. This type places the order.
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

    /// <summary>Builds the named, already-governed client every call below uses.</summary>
    private readonly IHttpClientFactory _httpClientFactory;

    /// <summary>The authority's address and the panel's contact details.</summary>
    private readonly AcmeOptions _options;

    /// <summary>The panel's ACME registration, created on first use and reused after.</summary>
    private readonly AcmeAccountStore _accountStore;

    /// <summary>Where the authority's own text goes, since a returned <see cref="Error"/> carries only a code.</summary>
    private readonly ILogger<AcmeClient> _logger;

    /// <summary>Proves control of a plain order's name with a file in its document root.</summary>
    private readonly AcmeHttpChallengeResponder _httpResponder;

    /// <summary>Proves control of a wildcard order's names with TXT records in a zone this server serves.</summary>
    private readonly AcmeDnsChallengeResponder _dnsResponder;

    /// <summary>Finalizes a validated order and downloads the chain.</summary>
    private readonly AcmeOrderFinalizer _finalizer;

    /// <summary>Creates the client.</summary>
    /// <param name="httpClientFactory">Supplies the named, pipeline-wrapped ACME client.</param>
    /// <param name="options">The authority's address and the panel's contact details.</param>
    /// <param name="challengeWriter">Places and removes the HTTP-01 challenge file.</param>
    /// <param name="dnsChallengeWriter">Publishes and removes the DNS-01 challenge records of a wildcard order.</param>
    /// <param name="accountStore">The panel's ACME registration.</param>
    /// <param name="clock">The injected time source, which bounds every wait below.</param>
    /// <param name="logger">Sink for the authority's diagnostic text.</param>
    public AcmeClient(
        IHttpClientFactory httpClientFactory,
        IOptions<AcmeOptions> options,
        IAcmeChallengeWriter challengeWriter,
        IAcmeDnsChallengeWriter dnsChallengeWriter,
        AcmeAccountStore accountStore,
        IClock clock,
        ILogger<AcmeClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _accountStore = accountStore;
        _logger = logger;
        var poller = new AcmeAuthorizationPoller(_options, clock);
        _httpResponder = new AcmeHttpChallengeResponder(challengeWriter, poller);
        _dnsResponder = new AcmeDnsChallengeResponder(dnsChallengeWriter, poller, logger);
        _finalizer = new AcmeOrderFinalizer(_options, clock, logger);
    }

    /// <inheritdoc />
    public async Task<Result<IssuedCertificate>> OrderAsync(
        AcmeOrderRequest request,
        CancellationToken cancellationToken)
    {
        using var http = _httpClientFactory.CreateClient(AcmeOptions.HttpClientName);

        var fetched = await AcmeDirectoryReader.ReadAsync(http, _options.DirectoryUrl, _logger, cancellationToken);
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
        await session.RefreshNonceAsync(AcmeJson.Text(directory, "newNonce"), cancellationToken);

        return await PlaceOrderAsync(session, directory, request, cancellationToken);
    }

    /// <summary>Runs the order from creation to issued material.</summary>
    /// <param name="session">The signed conversation with the authority.</param>
    /// <param name="directory">The authority's directory document.</param>
    /// <param name="request">The domain and where its challenge file is placed.</param>
    /// <param name="cancellationToken">Cancellation for the whole order.</param>
    /// <returns>The issued material, or a typed failure.</returns>
    private async Task<Result<IssuedCertificate>> PlaceOrderAsync(
        AcmeSession session,
        JsonElement directory,
        AcmeOrderRequest request,
        CancellationToken cancellationToken)
    {
        // A wildcard order names the domain AND its wildcard: the wildcard alone would leave the bare
        // domain, which a visitor types as often, outside the certificate. Both are proven at one label.
        string[] names = request.CoversWildcard ? [request.Domain, "*." + request.Domain] : [request.Domain];
        var identifiers = JsonSerializer.Serialize(new
        {
            identifiers = names.Select(name =>
            {
                return new { type = "dns", value = name };
            }).ToArray(),
        });

        var order = await session.PostAsync(AcmeJson.Text(directory, "newOrder"), identifiers, cancellationToken);
        if (!order.IsSuccess)
        {
            return Fail(request.Domain, "newOrder", order.Error!);
        }

        var orderUrl = order.Value.Location;
        var authorizationUrls = AuthorizationUrls(order.Value.Body);
        if (authorizationUrls.Count == 0 || orderUrl.Length == 0)
        {
            return Fail(
                request.Domain,
                "newOrder",
                Error.Of(nameof(ErrorMessages.AcmeOrderRejected), ErrorType.UpstreamRefused));
        }

        // RFC 8555 §7.1.6: an order for an identifier this account has ALREADY validated comes back
        // "ready" with nothing to prove, because authorities cache authorizations (Let's Encrypt for
        // thirty days). Renewal is exactly that case, so this branch is the module's main path and
        // not an edge: without it, renewal writes a challenge file, POSTs to a challenge on a valid
        // authorization, is answered "malformed", and throws away an order that was ready to finalize.
        if (!string.Equals(AcmeJson.Text(order.Value.Body, "status"), AcmeProtocol.OrderReady, StringComparison.Ordinal))
        {
            // EVERY authorization is answered, not the first: a wildcard order has one for the domain and
            // one for the wildcard, and finalizing with one of them still pending is refused.
            IAcmeChallengeResponder responder = request.CoversWildcard ? _dnsResponder : _httpResponder;
            var validated = await responder.RespondAsync(session, authorizationUrls, request, cancellationToken);
            if (!validated.IsSuccess)
            {
                return Result<IssuedCertificate>.Fail(validated.Error!);
            }
        }

        return await _finalizer.FinalizeAsync(session, order.Value.Body, orderUrl, names, cancellationToken);
    }

    /// <summary>Reads every authorization URL out of a newly created order.</summary>
    /// <param name="order">The order object the authority returned.</param>
    /// <returns>The URLs in the order's order; empty when the order carries none.</returns>
    private static List<string> AuthorizationUrls(JsonElement order)
    {
        if (!order.TryGetProperty("authorizations", out var authorizations)
            || authorizations.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return authorizations.EnumerateArray()
            .Select(authorization =>
            {
                return authorization.GetString() ?? string.Empty;
            })
            .Where(url =>
            {
                return url.Length > 0;
            })
            .ToList();
    }

    /// <summary>Logs one failed stage and returns it as the typed failure.</summary>
    /// <param name="domain">The domain the order was for.</param>
    /// <param name="stage">Which step refused, so an operator can tell an unreachable authority from a rejected order.</param>
    /// <param name="error">The typed failure to answer with, code and kind together.</param>
    /// <returns>The failed result carrying <paramref name="error"/>.</returns>
    private Result<IssuedCertificate> Fail(string domain, string stage, Error error)
    {
        AcmeOrderLog.Failed(_logger, domain, stage, error.Code);
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
