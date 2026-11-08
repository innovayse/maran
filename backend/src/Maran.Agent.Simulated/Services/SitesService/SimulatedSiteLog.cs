using System.Globalization;
using Maran.Agent.Client.Services.SitesService;
using Maran.Agent.Simulated.Usage;

namespace Maran.Agent.Simulated.Services.SitesService;

/// <summary>
/// Writes the log lines a simulated site "has served": believable access and error lines that depend
/// on the domain, the position and the moment, and on nothing else.
/// </summary>
/// <remarks>
/// Client addresses are drawn from the documentation range 203.0.113.0/24 (RFC 5737), never from an
/// address that could belong to a person, and the paths and agents are fixed vocabularies. A line is a
/// pure function of its arguments, so the same site tailed twice at the same moment shows the same log.
/// </remarks>
public static class SimulatedSiteLog
{
    /// <summary>The request paths a simulated site "serves".</summary>
    private static readonly string[] Paths =
    [
        "/",
        "/index.php",
        "/about",
        "/contact",
        "/blog/hello-world",
        "/assets/app.css",
        "/assets/app.js",
        "/favicon.ico",
        "/robots.txt",
    ];

    /// <summary>The response statuses a simulated site answers with, weighted by repetition.</summary>
    private static readonly int[] Statuses = [200, 200, 200, 200, 304, 301, 404];

    /// <summary>The user agents the simulated visitors send.</summary>
    private static readonly string[] Agents =
    [
        "Mozilla/5.0 (X11; Linux x86_64; rv:128.0) Gecko/20100101 Firefox/128.0",
        "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 Safari/605.1.15",
        "curl/8.5.0",
        "Mozilla/5.0 (compatible; ExampleBot/1.0)",
    ];

    /// <summary>The severities a simulated error log carries.</summary>
    private static readonly string[] ErrorLevels = ["notice", "warn", "error"];

    /// <summary>One line of the log <paramref name="source"/> for <paramref name="domain"/>.</summary>
    /// <param name="domain">The site's primary domain.</param>
    /// <param name="source">Which log the line belongs to.</param>
    /// <param name="index">How many lines before "now" this one is; larger is older.</param>
    /// <param name="now">The moment the newest line is stamped with; each older line is a minute earlier.</param>
    /// <returns>The line, without a trailing newline.</returns>
    public static string Line(string domain, SiteLogSource source, int index, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(domain);

        var hash = StableHash.Of(domain + "/" + index.ToString(CultureInfo.InvariantCulture));
        var at = now.AddMinutes(-index);

        return source == SiteLogSource.Access ? AccessLine(domain, hash, at) : ErrorLine(domain, hash, at);
    }

    /// <summary>Builds a combined-format access line.</summary>
    /// <param name="domain">The site's primary domain.</param>
    /// <param name="hash">The line's deterministic seed.</param>
    /// <param name="at">The line's timestamp.</param>
    /// <returns>The line.</returns>
    private static string AccessLine(string domain, ulong hash, DateTimeOffset at)
    {
        var address = "203.0.113." + (1 + (hash % 250)).ToString(CultureInfo.InvariantCulture);
        var stamp = at.UtcDateTime.ToString("dd/MMM/yyyy:HH:mm:ss", CultureInfo.InvariantCulture);
        var path = Paths[(int)((hash >> 8) % (ulong)Paths.Length)];
        var status = Statuses[(int)((hash >> 16) % (ulong)Statuses.Length)];
        var bytes = status == 304 ? 0UL : 300 + ((hash >> 24) % 48000);
        var agent = Agents[(int)((hash >> 40) % (ulong)Agents.Length)];

        var request = $"\"GET {path} HTTP/1.1\" {status} {bytes}";
        var referrer = $"\"https://{domain}/\" \"{agent}\"";

        return $"{address} - - [{stamp} +0000] {request} {referrer}";
    }

    /// <summary>Builds an error-log line.</summary>
    /// <param name="domain">The site's primary domain.</param>
    /// <param name="hash">The line's deterministic seed.</param>
    /// <param name="at">The line's timestamp.</param>
    /// <returns>The line.</returns>
    private static string ErrorLine(string domain, ulong hash, DateTimeOffset at)
    {
        var address = "203.0.113." + (1 + (hash % 250)).ToString(CultureInfo.InvariantCulture);
        var stamp = at.UtcDateTime.ToString("yyyy/MM/dd HH:mm:ss", CultureInfo.InvariantCulture);
        var level = ErrorLevels[(int)((hash >> 8) % (ulong)ErrorLevels.Length)];
        var path = Paths[(int)((hash >> 16) % (ulong)Paths.Length)];

        var failure = $"open() \"{path}\" failed (2: No such file or directory)";
        var origin = $"client: {address}, server: {domain}, request: \"GET {path} HTTP/1.1\"";

        return $"{stamp} [{level}] 1#1: *{hash % 9000} {failure}, {origin}";
    }
}
