using System.Net;
using System.Net.Http.Headers;
using Maran.Modules.Marketplace.Domain.Enums;
using Maran.Modules.Marketplace.Domain.Policies;
using Maran.Modules.Marketplace.Interfaces;
using Maran.Modules.Marketplace.Mappers;
using Maran.Modules.Marketplace.Models;
using Maran.Modules.Marketplace.Options;
using Maran.Sdk.Interfaces;
using Microsoft.Extensions.Logging;

namespace Maran.Modules.Marketplace.Services;

/// <summary>
/// The only type in the module that creates an HTTP client. Everything the panel says to the marketplace
/// host goes through it, so the rules of that conversation are written once: one host, https only,
/// no redirect it did not follow itself, every body capped while it is read, every request on a deadline.
/// </summary>
/// <remarks>
/// <para>
/// <b>Redirects.</b> The named client is built with automatic redirects off
/// (<see cref="CreateHandler"/>). The transport follows at most three itself, only for a GET, and only
/// while the next address is on the configured host over https; a redirect anywhere else is
/// <c>MarketplaceRedirectRefused</c>. A POST is never redirected: the grant request is single use and is
/// not repeated at an address a response chose.
/// </para>
/// <para>
/// <b>Retries.</b> A GET is retried up to two more times when the host could not be reached or answered
/// 408, 429 or a 5xx, with a short growing pause. A POST is never retried. The retry is here and not in the
/// Host's pipeline for the reason the ACME transport gives: the pipeline sees an exception outcome with no
/// request, so it cannot tell a repeatable GET from the single-use grant POST, and this type can.
/// </para>
/// <para>
/// <b>The lease credential</b> is attached through <see cref="ILeaseRequestAuthorizer"/> only to a request
/// that asks for it (<see cref="MarketplaceRequest.Lease"/>); this type never sees the token. The only
/// other thing sent is the interface language and the panel version: no host name, no account, no
/// identifier.
/// </para>
/// <para>
/// <b>Logging.</b> A failure is logged by attempt number alone. The address of a request can hold a grant
/// id and an exception message can quote it, so neither is written down, and no body ever is.
/// </para>
/// </remarks>
public sealed class MarketplaceHttpTransport : IMarketplaceTransport
{
    /// <summary>The response header carrying the detached signature (base64 DER).</summary>
    public const string SignatureHeader = "X-Maran-Signature";

    /// <summary>The request header carrying the panel version, so the service can list what runs on it.</summary>
    private const string PanelVersionHeader = "X-Maran-Panel-Version";

    /// <summary>The most redirects followed for one request.</summary>
    private const int MaxRedirects = 3;

    /// <summary>The most attempts a GET gets, the first included.</summary>
    private const int MaxAttempts = 3;

    /// <summary>The size of one read of a response body.</summary>
    private const int ReadChunkBytes = 16 * 1024;

    /// <summary>The pause before the second attempt; the third waits twice as long.</summary>
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(200);

    /// <summary>The longest a request waits on a host's <c>Retry-After</c> in all, however many attempts it makes.</summary>
    private static readonly TimeSpan MaxTotalRetryWait = TimeSpan.FromSeconds(5);

    /// <summary>
    /// The longest one pause between two attempts is: the total wait shared by the pauses a request can make, so
    /// a request that is told to wait every time still holds the caller for no more than <see cref="MaxTotalRetryWait"/>.
    /// </summary>
    private static readonly TimeSpan MaxRetryAfter = MaxTotalRetryWait / (MaxAttempts - 1);

    /// <summary>How long a new connection may take to be made.</summary>
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(10);

    /// <summary>Pre-compiled log delegate for an attempt that failed at the transport.</summary>
    private static readonly Action<ILogger, int, Exception?> LogAttemptFailed =
        LoggerMessage.Define<int>(
            LogLevel.Debug,
            new EventId(1, nameof(LogAttemptFailed)),
            "A marketplace request failed at the transport on attempt {Attempt}");

    /// <summary>Pre-compiled log delegate for a redirect that was not followed.</summary>
    private static readonly Action<ILogger, Exception?> LogRedirectRefused =
        LoggerMessage.Define(
            LogLevel.Warning,
            new EventId(2, nameof(LogRedirectRefused)),
            "A marketplace redirect was refused: it left the configured host, was not allowed, or went past the limit");

    /// <summary>The factory the named client comes from.</summary>
    private readonly IHttpClientFactory _factory;

    /// <summary>The options reader.</summary>
    private readonly MarketplaceOptionsReader _options;

    /// <summary>The running panel's version.</summary>
    private readonly IPanelVersion _panelVersion;

    /// <summary>Attaches the lease credential to a request that needs it.</summary>
    private readonly ILeaseRequestAuthorizer _lease;

    /// <summary>Operator log.</summary>
    private readonly ILogger<MarketplaceHttpTransport> _logger;

    /// <summary>Creates the transport.</summary>
    /// <param name="factory">The factory the named client comes from.</param>
    /// <param name="options">The options reader.</param>
    /// <param name="panelVersion">The running panel's version.</param>
    /// <param name="lease">Attaches the lease credential to a request that needs it.</param>
    /// <param name="logger">Operator log.</param>
    public MarketplaceHttpTransport(
        IHttpClientFactory factory,
        MarketplaceOptionsReader options,
        IPanelVersion panelVersion,
        ILeaseRequestAuthorizer lease,
        ILogger<MarketplaceHttpTransport> logger)
    {
        _factory = factory;
        _options = options;
        _panelVersion = panelVersion;
        _lease = lease;
        _logger = logger;
    }

    /// <summary>Builds the handler under the named client: no automatic redirects, no cookies, a bounded connect.</summary>
    /// <returns>The handler the factory uses as the innermost one.</returns>
    public static HttpMessageHandler CreateHandler()
    {
        return new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            ConnectTimeout = ConnectTimeout,
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        };
    }

    /// <summary>
    /// Configures the named client. Its own timeout is infinite on purpose: the deadline is the options'
    /// <c>RequestTimeoutSeconds</c>, applied to each attempt here and by the Host's pipeline, and a
    /// second, different budget around them would make the configured one silently not apply.
    /// </summary>
    /// <param name="client">The client being created.</param>
    public static void ConfigureClient(HttpClient client)
    {
        client.Timeout = Timeout.InfiniteTimeSpan;
    }

    /// <inheritdoc />
    public async Task<Result<MarketplaceResponse>> SendAsync(MarketplaceRequest request, CancellationToken ct)
    {
        var settings = _options.Read();
        if (!settings.IsSuccess)
        {
            return Result<MarketplaceResponse>.Fail(settings.Error!);
        }

        var options = settings.Value;
        var baseUri = MarketplaceHostPolicy.BaseUri(options);

        // A path that names another host ("//other.example/x") would resolve off the allow-list; judge the result.
        if (!Uri.TryCreate(baseUri, request.PathAndQuery, out var target)
            || !MarketplaceHostPolicy.IsConfiguredHost(target, baseUri))
        {
            return Result<MarketplaceResponse>.Fail(MarketplaceClientRefusals.RequestRefused);
        }

        var cap = request.Limit == ResponseLimit.Grant ? options.MaxGrantBytes : options.MaxCatalogueBytes;
        var timeout = TimeSpan.FromSeconds(options.RequestTimeoutSeconds);
        var http = _factory.CreateClient(MarketplaceOptions.HttpClientName);
        var isGet = request.Method == HttpMethod.Get;

        var followed = await FollowAsync(
            http,
            baseUri,
            target,
            follow: isGet,
            retry: isGet,
            timeout,
            (uri, token) => { return BuildAsync(request, uri, token); },
            ct);
        if (!followed.IsSuccess)
        {
            return Result<MarketplaceResponse>.Fail(followed.Error!);
        }

        using var response = followed.Value;
        return await ReadAsync(response, cap, timeout, ct);
    }

    /// <inheritdoc />
    public async Task<Result<MarketplaceDownload>> OpenDownloadAsync(Uri url, CancellationToken ct)
    {
        var settings = _options.Read();
        if (!settings.IsSuccess)
        {
            return Result<MarketplaceDownload>.Fail(settings.Error!);
        }

        var options = settings.Value;
        var baseUri = MarketplaceHostPolicy.BaseUri(options);
        if (!MarketplaceHostPolicy.IsConfiguredHost(url, baseUri))
        {
            return Result<MarketplaceDownload>.Fail(MarketplaceClientRefusals.GrantHostRefused);
        }

        var timeout = TimeSpan.FromSeconds(options.RequestTimeoutSeconds);
        var http = _factory.CreateClient(MarketplaceOptions.HttpClientName);

        var followed = await FollowAsync(
            http,
            baseUri,
            url,
            follow: true,
            retry: false,
            timeout,
            (uri, _) => { return Task.FromResult(Result<HttpRequestMessage>.Ok(BuildDownload(uri))); },
            ct);
        if (!followed.IsSuccess)
        {
            return Result<MarketplaceDownload>.Fail(followed.Error!);
        }

        var response = followed.Value;
        if (response.StatusCode != HttpStatusCode.OK)
        {
            // The refusal's problem document says which grant failure it is; it is read under the grant cap.
            var problem = await ReadAsync(response, options.MaxGrantBytes, timeout, ct);
            var status = (int)response.StatusCode;
            response.Dispose();
            return Result<MarketplaceDownload>.Fail(
                MarketplaceProblemMapper.ToDownloadError(status, problem.IsSuccess ? problem.Value.Body : Array.Empty<byte>()));
        }

        try
        {
            var stream = await response.Content.ReadAsStreamAsync(ct);
            return Result<MarketplaceDownload>.Ok(
                new MarketplaceDownload(stream, response.Content.Headers.ContentLength, response));
        }
        catch (HttpRequestException)
        {
            response.Dispose();
            return Result<MarketplaceDownload>.Fail(MarketplaceClientRefusals.Unreachable);
        }
        catch (IOException)
        {
            response.Dispose();
            return Result<MarketplaceDownload>.Fail(MarketplaceClientRefusals.Unreachable);
        }
    }

    /// <summary>
    /// How long to wait before an attempt. A host that says when to come back (<c>Retry-After</c> in
    /// seconds) is believed, but never for longer than <see cref="MaxRetryAfter"/>, so a hostile or mistaken
    /// header cannot hold a request open; otherwise the short growing pause applies.
    /// </summary>
    /// <param name="nextAttempt">The number of the attempt about to be made (2 or 3).</param>
    /// <param name="retryAfter">The failed answer's <c>Retry-After</c> header, or null.</param>
    /// <returns>The pause.</returns>
    public static TimeSpan PauseBefore(int nextAttempt, RetryConditionHeaderValue? retryAfter)
    {
        if (retryAfter?.Delta is { } asked && asked >= TimeSpan.Zero)
        {
            return asked < MaxRetryAfter ? asked : MaxRetryAfter;
        }

        return RetryDelay * (nextAttempt - 1);
    }

    /// <summary>Whether a status is one of the redirects the transport recognises.</summary>
    /// <param name="status">The status.</param>
    /// <returns>True for 301, 302, 303, 307 and 308.</returns>
    private static bool IsRedirect(HttpStatusCode status)
    {
        return (int)status is 301 or 302 or 303 or 307 or 308;
    }

    /// <summary>Whether a status is worth asking again for a repeatable request.</summary>
    /// <param name="status">The status.</param>
    /// <returns>True for 408, 429, 500, 502, 503 and 504.</returns>
    private static bool IsRetryable(HttpStatusCode status)
    {
        return (int)status is 408 or 429 or 500 or 502 or 503 or 504;
    }

    /// <summary>Resolves a redirect target and keeps it only when it is still on the configured host.</summary>
    /// <param name="current">The address that redirected.</param>
    /// <param name="location">The <c>Location</c> header, absolute or relative.</param>
    /// <param name="baseUri">The configured address.</param>
    /// <returns>The next address, or null when it leaves the host or is not https.</returns>
    private static Uri? ResolveRedirect(Uri current, Uri location, Uri baseUri)
    {
        var next = location.IsAbsoluteUri ? location : new Uri(current, location);
        return MarketplaceHostPolicy.IsConfiguredHost(next, baseUri) ? next : null;
    }

    /// <summary>Builds the GET for a package download. It carries no lease and no panel version: the grant is the credential.</summary>
    /// <param name="uri">The download address.</param>
    /// <returns>The request message.</returns>
    private static HttpRequestMessage BuildDownload(Uri uri)
    {
        var message = new HttpRequestMessage(HttpMethod.Get, uri);
        message.Headers.Accept.ParseAdd("application/octet-stream");
        return message;
    }

    /// <summary>Reads a response body under the size cap and a deadline.</summary>
    /// <param name="response">The response, whose headers have arrived.</param>
    /// <param name="cap">The most bytes accepted.</param>
    /// <param name="timeout">The deadline for reading the body.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The answer, or a coded failure when the body is over the cap or the read failed.</returns>
    private static async Task<Result<MarketplaceResponse>> ReadAsync(
        HttpResponseMessage response,
        long cap,
        TimeSpan timeout,
        CancellationToken ct)
    {
        if (response.Content.Headers.ContentLength is { } declared && declared > cap)
        {
            return Result<MarketplaceResponse>.Fail(MarketplaceClientRefusals.ResponseTooLarge);
        }

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(timeout);

        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(deadline.Token);
            using var body = new MemoryStream();
            var chunk = new byte[ReadChunkBytes];

            while (true)
            {
                // One byte past the cap is enough to know the body is too long; never read more.
                var wanted = (int)Math.Min(chunk.Length, cap + 1 - body.Length);
                var read = await stream.ReadAsync(chunk.AsMemory(0, wanted), deadline.Token);
                if (read == 0)
                {
                    break;
                }

                body.Write(chunk, 0, read);
                if (body.Length > cap)
                {
                    return Result<MarketplaceResponse>.Fail(MarketplaceClientRefusals.ResponseTooLarge);
                }
            }

            return Result<MarketplaceResponse>.Ok(new MarketplaceResponse(
                (int)response.StatusCode,
                body.ToArray(),
                response.Headers.TryGetValues(SignatureHeader, out var values) ? values.FirstOrDefault() : null,
                response.Headers.ETag?.ToString()));
        }
        catch (HttpRequestException)
        {
            return Result<MarketplaceResponse>.Fail(MarketplaceClientRefusals.Unreachable);
        }
        catch (IOException)
        {
            return Result<MarketplaceResponse>.Fail(MarketplaceClientRefusals.Unreachable);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return Result<MarketplaceResponse>.Fail(MarketplaceClientRefusals.Unreachable);
        }
    }

    /// <summary>Builds one JSON request message, attaching the lease credential when the request asks for it.</summary>
    /// <param name="request">What to send.</param>
    /// <param name="uri">The address of this hop.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The message, or <c>LeaseRequired</c> when the request needs a credential none could be attached.</returns>
    private async Task<Result<HttpRequestMessage>> BuildAsync(MarketplaceRequest request, Uri uri, CancellationToken ct)
    {
        var message = new HttpRequestMessage(request.Method, uri);
        message.Headers.Accept.ParseAdd("application/json");
        message.Headers.TryAddWithoutValidation(PanelVersionHeader, _panelVersion.Current);

        if (!string.IsNullOrWhiteSpace(request.Locale))
        {
            message.Headers.AcceptLanguage.TryParseAdd(request.Locale);
        }

        if (request.IfNoneMatch is not null)
        {
            message.Headers.IfNoneMatch.TryParseAdd(request.IfNoneMatch);
        }

        if (request.Body is not null)
        {
            var content = new ByteArrayContent(request.Body);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            message.Content = content;
        }

        if (request.Lease != LeaseRequirement.None)
        {
            var attached = await _lease.TryAuthorizeAsync(message, ct);
            if (!attached && request.Lease == LeaseRequirement.Required)
            {
                message.Dispose();
                return Result<HttpRequestMessage>.Fail(MarketplaceClientRefusals.LeaseRequired);
            }
        }

        return Result<HttpRequestMessage>.Ok(message);
    }

    /// <summary>Sends a request and follows the redirects the rules allow.</summary>
    /// <param name="http">The named client.</param>
    /// <param name="baseUri">The configured address.</param>
    /// <param name="first">The first address.</param>
    /// <param name="follow">Whether a redirect may be followed (a GET); a redirect of anything else is refused.</param>
    /// <param name="retry">Whether the request may be sent again when the host cannot be reached (a repeatable GET).</param>
    /// <param name="timeout">The deadline for one attempt.</param>
    /// <param name="build">Builds the message for an address.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The final, non-redirect response, which the caller disposes; or a coded failure.</returns>
    private async Task<Result<HttpResponseMessage>> FollowAsync(
        HttpClient http,
        Uri baseUri,
        Uri first,
        bool follow,
        bool retry,
        TimeSpan timeout,
        Func<Uri, CancellationToken, Task<Result<HttpRequestMessage>>> build,
        CancellationToken ct)
    {
        var current = first;

        for (var hop = 0; ; hop++)
        {
            var attempt = await AttemptAsync(http, current, retry, timeout, build, ct);
            if (!attempt.IsSuccess || !IsRedirect(attempt.Value.StatusCode))
            {
                return attempt;
            }

            var response = attempt.Value;
            var next = response.Headers.Location is { } location ? ResolveRedirect(current, location, baseUri) : null;
            response.Dispose();

            if (!follow || hop >= MaxRedirects || next is null)
            {
                LogRedirectRefused(_logger, null);
                return Result<HttpResponseMessage>.Fail(MarketplaceClientRefusals.RedirectRefused);
            }

            current = next;
        }
    }

    /// <summary>Sends one request, asking again for a repeatable one when the host could not be reached or is struggling.</summary>
    /// <param name="http">The named client.</param>
    /// <param name="uri">The address.</param>
    /// <param name="retry">Whether the request may be sent more than once.</param>
    /// <param name="timeout">The deadline for one attempt.</param>
    /// <param name="build">Builds the message; called afresh for every attempt, because a message cannot be sent twice.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The response of the last attempt, whatever its status, or <c>MarketplaceUnreachable</c>.</returns>
    private async Task<Result<HttpResponseMessage>> AttemptAsync(
        HttpClient http,
        Uri uri,
        bool retry,
        TimeSpan timeout,
        Func<Uri, CancellationToken, Task<Result<HttpRequestMessage>>> build,
        CancellationToken ct)
    {
        var attempts = retry ? MaxAttempts : 1;

        for (var attempt = 1; ; attempt++)
        {
            var built = await build(uri, ct);
            if (!built.IsSuccess)
            {
                return Result<HttpResponseMessage>.Fail(built.Error!);
            }

            HttpResponseMessage? response = null;

            using (var message = built.Value)
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                deadline.CancelAfter(timeout);

                try
                {
                    response = await http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
                }
                catch (HttpRequestException)
                {
                    response = null;
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    response = null;
                }
            }

            if (response is not null && (attempt >= attempts || !IsRetryable(response.StatusCode)))
            {
                return Result<HttpResponseMessage>.Ok(response);
            }

            LogAttemptFailed(_logger, attempt, null);
            var pause = PauseBefore(attempt + 1, response?.Headers.RetryAfter);
            response?.Dispose();

            if (attempt >= attempts)
            {
                return Result<HttpResponseMessage>.Fail(MarketplaceClientRefusals.Unreachable);
            }

            await Task.Delay(pause, ct);
        }
    }
}
