using System.Net;
using System.Net.Http.Headers;
using Maran.Modules.ServerMigration.Domain.Enums;
using Maran.Modules.ServerMigration.Domain.Policies;
using Maran.Modules.ServerMigration.Interfaces;
using Maran.Modules.ServerMigration.Resources;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Security;
using Maran.SharedKernel.Utilities.Network;
using Microsoft.Extensions.Logging;

namespace Maran.Modules.ServerMigration.Services;

/// <summary>
/// The receiving panel's outbound HTTPS request (issue #127; rules/security.md item 10): one GET to the origin an
/// administrator typed, through the named client whose connector refuses a forbidden address and whose pipeline
/// bounds the wait for headers.
/// </summary>
/// <remarks>
/// <para>
/// <b>What it sends.</b> A GET with the grant in <c>Authorization: Bearer</c> and nothing else: no account, no
/// host name, no panel identifier. The grant is never in the URL, never logged (the failure paths log a fixed
/// phrase and the status), and is dropped from memory only when the garbage collector takes the request.
/// </para>
/// <para>
/// <b>What it refuses.</b> A plain-http or malformed origin (the caller normalised it, and this checks again, so
/// a call that skipped the normaliser cannot send a grant in clear); a redirect (not followed: any 3xx is a
/// refusal, because following one would send the grant to a host nobody typed); a status other than 200. The
/// answer to a refused grant is one not-found-shaped status from the other panel, which is reported as the
/// grant being refused, never as which condition failed.
/// </para>
/// <para>
/// <b>Bounds.</b> The headers must arrive within <see cref="MigrationPullLimits.HeaderTimeout"/>; each read of
/// the body within <see cref="MigrationPullLimits.ReadStall"/> (<see cref="PulledPackageStream"/>); the body as
/// a whole by the length the signed manifest declares, which the caller enforces.
/// </para>
/// </remarks>
public sealed class MigrationPullClient : IMigrationPullClient
{
    /// <summary>The name of the HTTP client this class sends through.</summary>
    public const string HttpClientName = "migration-pull";

    /// <summary>The path of the head endpoint on the exporting panel.</summary>
    private const string HeadPath = "/api/v1/migration-pull/head";

    /// <summary>The path of the package endpoint on the exporting panel.</summary>
    private const string PackagePath = "/api/v1/migration-pull/package";

    /// <summary>Pre-compiled log delegate for a pull that failed. Carries a status or a phrase and never the grant.</summary>
    private static readonly Action<ILogger, string, Exception?> LogFailed =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(1, nameof(MigrationPullClient)),
            "A pull from another panel failed: {Reason}");

    /// <summary>The factory the named client comes from.</summary>
    private readonly IHttpClientFactory _factory;

    /// <summary>Operator log.</summary>
    private readonly ILogger<MigrationPullClient> _logger;

    /// <summary>Creates the client.</summary>
    /// <param name="factory">The factory the named client comes from.</param>
    /// <param name="logger">Operator log.</param>
    public MigrationPullClient(IHttpClientFactory factory, ILogger<MigrationPullClient> logger)
    {
        _factory = factory;
        _logger = logger;
    }

    /// <summary>Configures the named client. Its own timeout is infinite on purpose: the deadlines are the header deadline and the read stall, applied here, and the pipeline's backstop.</summary>
    /// <param name="client">The client being created.</param>
    public static void ConfigureClient(HttpClient client)
    {
        client.Timeout = Timeout.InfiniteTimeSpan;
    }

    /// <inheritdoc />
    public async Task<Result<Stream>> OpenAsync(
        string origin,
        PullPart part,
        SensitiveString grant,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(origin);
        ArgumentNullException.ThrowIfNull(grant);

        // Checked again here on purpose: the one place a grant leaves the process must not rely on a caller
        // having normalised the address.
        if (!PublicUrlRule.TryNormalise(origin, out var normalised)
            || !Uri.TryCreate(normalised + (part == PullPart.Head ? HeadPath : PackagePath), UriKind.Absolute, out var target))
        {
            return Result<Stream>.Fail(Error.Of(nameof(ErrorMessages.MigrationPullAddressRefused), ErrorType.Validation));
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, target);
        request.Headers.TryAddWithoutValidation("Authorization", MigrationGrantDefaults.BearerPrefix + grant.Reveal());
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));

        var http = _factory.CreateClient(HttpClientName);
        using var headerDeadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        headerDeadline.CancelAfter(MigrationPullLimits.HeaderTimeout);

        HttpResponseMessage response;

        try
        {
            response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, headerDeadline.Token);
        }
        catch (HttpRequestException exception) when (PullConnector.IsAddressRefusal(exception))
        {
            LogFailed(_logger, "the destination address is not allowed", null);

            return Result<Stream>.Fail(Error.Of(nameof(ErrorMessages.MigrationPullAddressRefused), ErrorType.Validation));
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException or IOException
            && !cancellationToken.IsCancellationRequested)
        {
            LogFailed(_logger, "the other server could not be reached", null);

            return Result<Stream>.Fail(Error.Of(nameof(ErrorMessages.MigrationPullUnreachable), ErrorType.Unavailable));
        }

        if (response.StatusCode != HttpStatusCode.OK)
        {
            var refused = response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.NotFound;
            LogFailed(_logger, $"the other server answered {(int)response.StatusCode}", null);
            response.Dispose();

            return Result<Stream>.Fail(refused
                ? Error.Of(nameof(ErrorMessages.MigrationPullRefused), ErrorType.Conflict)
                : Error.Of(nameof(ErrorMessages.MigrationPullUnreachable), ErrorType.Unavailable));
        }

        var body = await response.Content.ReadAsStreamAsync(cancellationToken);

        return Result<Stream>.Ok(new PulledPackageStream(response, body, MigrationPullLimits.ReadStall));
    }
}
