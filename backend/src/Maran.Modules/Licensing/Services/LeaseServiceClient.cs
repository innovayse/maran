using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Maran.Modules.Licensing.Domain.Enums;
using Maran.Modules.Licensing.Interfaces;
using Maran.Modules.Licensing.Models;
using Maran.Modules.Licensing.Options;
using Maran.SharedKernel.Utilities.Network;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Maran.Modules.Licensing.Services;

/// <summary>
/// The only type in the Licensing module that touches an HTTP client. Everything the panel says to the
/// licence service goes through it, so the rules of that one conversation are written once: one host chosen
/// by configuration, https only, a body that is the licence id and nothing else, no credential, no cookie, no
/// redirect, a response cut off at a bound while it is read, and a deadline on the request.
/// </summary>
/// <remarks>
/// <para>
/// <b>What leaves the server</b> (rules/security.md item 10, fifth exception): one <c>POST</c> to
/// <c>/api/v1/lease</c> with the body <c>{"licence":"&lt;licence id&gt;"}</c>. No panel version, no host name,
/// no account or customer data, no machine id, no <c>Authorization</c> header; the connection's source address
/// is visible to the service as with any HTTPS request. The licence id is already known to the service, which
/// issued the licence.
/// </para>
/// <para>
/// <b>Redirects are never followed</b> (the named client is built with automatic redirects off,
/// <see cref="CreateHandler"/>): a POST is not repeated at an address a response chose, and the allow-list is
/// the configured host and no other. A redirect is a refusal.
/// </para>
/// <para>
/// <b>Retries.</b> None here, and none in the pipeline: the worker asks again after
/// <see cref="Domain.Policies.LeaseRenewalPolicy"/>'s failure wait, hours later, so an outage costs a handful
/// of requests a day and not a burst.
/// </para>
/// <para>
/// <b>Logging.</b> A failure is logged by its exception type alone. The address, the licence id and the body
/// are never written down.
/// </para>
/// </remarks>
public sealed class LeaseServiceClient : ILeaseServiceClient
{
    /// <summary>How long a new connection may take to be made.</summary>
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(10);

    /// <summary>A request failed before an answer was in hand.</summary>
    private static readonly Action<ILogger, string, Exception?> LogRequestFailed =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(1, nameof(LogRequestFailed)),
            "The licence service could not be reached for a lease renewal ({ExceptionType}).");

    /// <summary>The factory the named client comes from.</summary>
    private readonly IHttpClientFactory _factory;

    /// <summary>Where the service is.</summary>
    private readonly LeaseServiceOptions _options;

    /// <summary>Operator log.</summary>
    private readonly ILogger<LeaseServiceClient> _logger;

    /// <summary>Creates the client.</summary>
    /// <param name="factory">The factory the named client comes from.</param>
    /// <param name="options">Where the service is.</param>
    /// <param name="logger">Operator log.</param>
    public LeaseServiceClient(
        IHttpClientFactory factory,
        IOptions<LeaseServiceOptions> options,
        ILogger<LeaseServiceClient> logger)
    {
        _factory = factory;
        _options = options.Value;
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
    /// <c>LeaseRequestTimeoutSeconds</c>, applied once by the Host's outbound pipeline, and a second, different
    /// budget around it would make the configured one silently not apply.
    /// </summary>
    /// <param name="client">The client being created.</param>
    public static void ConfigureClient(HttpClient client)
    {
        client.Timeout = Timeout.InfiniteTimeSpan;
    }

    /// <inheritdoc />
    public async Task<LeaseServiceAnswer> RequestAsync(string licenceId, CancellationToken cancellationToken)
    {
        if (!PublicUrlRule.TryNormalise(_options.LeaseServiceBaseUrl, out var origin)
            || !Uri.TryCreate(origin + LeaseServiceOptions.RequestPath, UriKind.Absolute, out var target))
        {
            return LeaseServiceAnswer.Without(LeaseServiceAnswerKind.NotConfigured);
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, target)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { licence = licenceId }),
                Encoding.UTF8,
                "application/json"),
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        try
        {
            var http = _factory.CreateClient(LeaseServiceOptions.HttpClientName);
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            if (IsUnreachable(response.StatusCode))
            {
                return LeaseServiceAnswer.Without(LeaseServiceAnswerKind.Unreachable);
            }

            if (response.StatusCode != HttpStatusCode.OK)
            {
                return LeaseServiceAnswer.Without(LeaseServiceAnswerKind.Refused);
            }

            return await ReadBodyAsync(response, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or TimeoutException or OperationCanceledException)
        {
            // No answer: the connection failed, the pipeline's deadline ended the request, or the body broke off.
            LogRequestFailed(_logger, exception.GetType().Name, null);

            return LeaseServiceAnswer.Without(LeaseServiceAnswerKind.Unreachable);
        }
    }

    /// <summary>Whether a status means the service did not answer in a way that says anything about the licence.</summary>
    /// <param name="status">The status.</param>
    /// <returns>True for 408, 429 and every 5xx.</returns>
    private static bool IsUnreachable(HttpStatusCode status)
    {
        return (int)status is 408 or 429 or >= 500;
    }

    /// <summary>Reads the body up to the bound and no further.</summary>
    /// <param name="response">A 200 answer whose headers have arrived.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The body as text, or <see cref="LeaseServiceAnswerKind.TooLarge"/> when it outgrew the bound.</returns>
    private static async Task<LeaseServiceAnswer> ReadBodyAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        const int limit = LeaseStorageOptions.MaxLeaseFileBytes;

        // A declared length over the bound is refused before a byte is read; a body that does not declare one,
        // or lies, is stopped at one byte past the bound below.
        if (response.Content.Headers.ContentLength > limit)
        {
            return LeaseServiceAnswer.Without(LeaseServiceAnswerKind.TooLarge);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var buffer = new byte[limit + 1];
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(total), cancellationToken);
            if (read == 0)
            {
                break;
            }

            total += read;
        }

        return total > limit
            ? LeaseServiceAnswer.Without(LeaseServiceAnswerKind.TooLarge)
            : new LeaseServiceAnswer(LeaseServiceAnswerKind.Received, Encoding.UTF8.GetString(buffer, 0, total));
    }
}
