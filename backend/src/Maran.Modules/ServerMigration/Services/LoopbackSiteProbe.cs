using System.Text;
using System.Text.RegularExpressions;
using Maran.Modules.ServerMigration.Interfaces;
using Maran.Modules.ServerMigration.Models;
using Maran.SharedKernel.Utilities.Network;

namespace Maran.Modules.ServerMigration.Services;

/// <summary>
/// The <see cref="ISiteProbe"/> that asks the panel's own web server on the loopback address, with the migrated
/// domain in the <c>Host</c> header (issue #197, phase 2).
/// </summary>
/// <remarks>
/// <para>
/// <b>The target is a constant.</b> <see cref="Origin"/> is the loopback address and the Host extension that
/// registers the named client takes its base address from it; the only text from outside that reaches the request
/// is the domain, which is a header value after <see cref="HostNameRule"/> accepted it. The DNS of the domain is
/// never consulted, so a domain that points at somewhere else is still fetched from here, which is the point of the
/// check: does THIS server serve the site.
/// </para>
/// <para>
/// A redirect is not followed (the client is built without redirects): a 301 to https is an answer from this server
/// and is reported as the status it is.
/// </para>
/// </remarks>
public sealed partial class LoopbackSiteProbe : ISiteProbe
{
    /// <summary>The name of the HTTP client the Host registers with its resilience pipeline.</summary>
    public const string HttpClientName = "servermigration-verify";

    /// <summary>The only address the probe connects to.</summary>
    public const string Origin = "http://127.0.0.1/";

    /// <summary>The most bytes of a page that are read to find its title.</summary>
    private const int BodyLimitBytes = 64 * 1024;

    /// <summary>The longest title that is kept.</summary>
    private const int TitleMaxLength = 160;

    /// <summary>Hands out the named client.</summary>
    private readonly IHttpClientFactory _clients;

    /// <summary>Creates the probe.</summary>
    /// <param name="clients">Hands out the named client.</param>
    public LoopbackSiteProbe(IHttpClientFactory clients)
    {
        _clients = clients;
    }

    /// <inheritdoc/>
    public async Task<SiteProbeResult> ProbeAsync(string domain, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(domain);

        if (!HostNameRule.IsHostName(domain))
        {
            return new SiteProbeResult(null, null);
        }

        try
        {
            var client = _clients.CreateClient(HttpClientName);
            using var request = new HttpRequestMessage(HttpMethod.Get, "/");
            request.Headers.Host = domain;

            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            var title = await ReadTitleAsync(response, cancellationToken);

            return new SiteProbeResult((int)response.StatusCode, title);
        }
        catch (HttpRequestException)
        {
            return new SiteProbeResult(null, null);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new SiteProbeResult(null, null);
        }
    }

    /// <summary>Reads the start of an HTML page and finds its title.</summary>
    /// <param name="response">The response.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The title, or <c>null</c>.</returns>
    private static async Task<string?> ReadTitleAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var mediaType = response.Content.Headers.ContentType?.MediaType;
        if (mediaType is null || !mediaType.Contains("html", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var buffer = new byte[BodyLimitBytes];
        var read = 0;
        while (read < buffer.Length)
        {
            var count = await stream.ReadAsync(buffer.AsMemory(read), cancellationToken);
            if (count == 0)
            {
                break;
            }

            read += count;
        }

        var match = TitlePattern().Match(Encoding.UTF8.GetString(buffer, 0, read));
        if (!match.Success)
        {
            return null;
        }

        var title = new string(match.Groups[1].Value.Where(character =>
        {
            return !char.IsControl(character);
        }).ToArray()).Trim();

        return title.Length == 0 ? null : title.Length > TitleMaxLength ? title[..TitleMaxLength] : title;
    }

    /// <summary>The pattern that finds the title element.</summary>
    /// <returns>The compiled pattern.</returns>
    [GeneratedRegex("<title[^>]*>(.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline, matchTimeoutMilliseconds: 500)]
    private static partial Regex TitlePattern();
}
